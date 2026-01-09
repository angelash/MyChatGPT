package com.audiobridge.client.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Binder
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.PowerManager
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.audiobridge.client.MainActivity
import com.audiobridge.client.R
import com.audiobridge.client.abp.AbpControlMessage
import com.audiobridge.client.abp.WelcomeMessage
import com.audiobridge.client.audio.AudioBridgeManager
import com.audiobridge.client.ws.AbpWebSocketClient
import java.util.UUID

/**
 * 前台服务：用于“退后台/锁屏仍保持语音桥接”。
 *
 * 说明：
 * - 连接/音频链路放在 Service 内，Activity 只负责 UI 与控制
 * - 通过前台通知保持进程优先级，避免后台限制导致录音/网络被停
 */
class AudioBridgeForegroundService : Service() {

    companion object {
        private const val TAG = "AudioBridgeFGS"

        const val ACTION_START = "com.audiobridge.client.action.START"
        const val ACTION_STOP = "com.audiobridge.client.action.STOP"

        const val EXTRA_HOST = "host"
        const val EXTRA_TOKEN = "token"
        const val EXTRA_ENABLE_UPLINK = "enableUplink"
        const val EXTRA_ENABLE_DOWNLINK = "enableDownlink"

        private const val CHANNEL_ID = "audiobridge"
        private const val NOTIFICATION_ID = 1001
    }

    data class Snapshot(
        val wsState: AbpWebSocketClient.State,
        val selectedCodec: String,
        val enableUplink: Boolean,
        val enableDownlink: Boolean,
        val captureRunning: Boolean,
        val playerRunning: Boolean,
        val playerBufferedMs: Int,
        val playerUnderrunCount: Long,
        val uplinkFramesCaptured: Long,
        val downlinkFramesPlayed: Long,
        val uplinkFramesSent: Long,
        val uplinkFramesSuppressed: Long,
        val uplinkBytesSent: Long,
        val downlinkFramesReceived: Long,
        val downlinkBytesReceived: Long,
        val lastError: String?,
    )

    inner class LocalBinder : Binder() {
        fun getService(): AudioBridgeForegroundService = this@AudioBridgeForegroundService
    }

    private val binder = LocalBinder()

    private val wsClient = AbpWebSocketClient()
    private val audioManager = AudioBridgeManager()
    private val mainHandler = Handler(Looper.getMainLooper())

    private var startedForeground = false
    private var wsState: AbpWebSocketClient.State = AbpWebSocketClient.State.DISCONNECTED
    private var host: String = ""
    private var token: String? = null
    private var enableUplink: Boolean = true
    private var enableDownlink: Boolean = true
    private var lastError: String? = null

    private var wakeLock: PowerManager.WakeLock? = null

    private val notificationTicker = object : Runnable {
        override fun run() {
            if (startedForeground) {
                updateNotification()
                mainHandler.postDelayed(this, 1000)
            }
        }
    }

    override fun onCreate() {
        super.onCreate()

        createNotificationChannel()

        audioManager.onUplinkFrame = { frame ->
            wsClient.sendUplinkFrame(frame)
        }
        audioManager.onError = { msg ->
            lastError = msg
            Log.e(TAG, "Audio error: $msg")
            updateNotification()
        }
    }

    override fun onBind(intent: Intent): IBinder = binder

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_START -> {
                host = intent.getStringExtra(EXTRA_HOST).orEmpty()
                token = intent.getStringExtra(EXTRA_TOKEN)?.takeIf { it.isNotBlank() }
                enableUplink = intent.getBooleanExtra(EXTRA_ENABLE_UPLINK, true)
                enableDownlink = intent.getBooleanExtra(EXTRA_ENABLE_DOWNLINK, true)

                ensureForegroundStarted()
                connectIfNeeded()
            }

            ACTION_STOP -> {
                Log.i(TAG, "Stop requested")
                disconnectAndStopSelf()
            }
        }

        // 前台服务正在运行时，系统如回收可尝试重启（此处不强行自动重连，避免无 UI 时反复打洞）
        return START_STICKY
    }

    override fun onDestroy() {
        super.onDestroy()
        mainHandler.removeCallbacks(notificationTicker)
        disconnectInternal()
        releaseWakeLock()
    }

    fun getSnapshot(): Snapshot {
        return Snapshot(
            wsState = wsState,
            selectedCodec = wsClient.selectedCodec,
            enableUplink = enableUplink,
            enableDownlink = enableDownlink,
            captureRunning = audioManager.isCaptureRunning,
            playerRunning = audioManager.isPlayerRunning,
            playerBufferedMs = audioManager.playerBufferedMs,
            playerUnderrunCount = audioManager.playerUnderrunCount,
            uplinkFramesCaptured = audioManager.uplinkFrames,
            downlinkFramesPlayed = audioManager.downlinkFrames,
            uplinkFramesSent = wsClient.uplinkFramesSentCount,
            uplinkFramesSuppressed = wsClient.uplinkFramesSuppressedCount,
            uplinkBytesSent = wsClient.uplinkPayloadBytesSentCount,
            downlinkFramesReceived = wsClient.downlinkFramesReceivedCount,
            downlinkBytesReceived = wsClient.downlinkPayloadBytesReceivedCount,
            lastError = lastError,
        )
    }

    fun requestStop() {
        disconnectAndStopSelf()
    }

    fun setEnableUplink(enabled: Boolean) {
        enableUplink = enabled
        // 只有在已连接（握手完成后音频链路存在）时才动态启停麦克风，避免“未连接但占用麦克风”
        if (wsState == AbpWebSocketClient.State.CONNECTED) {
            if (enabled) {
                audioManager.startCapture()
            } else {
                audioManager.stopCapture()
            }
        }
        updateNotification()
    }

    fun setEnableDownlink(enabled: Boolean) {
        enableDownlink = enabled
        // 只有在已连接时才动态启停播放器
        if (wsState == AbpWebSocketClient.State.CONNECTED) {
            if (enabled) {
                audioManager.startPlayer()
            } else {
                audioManager.stopPlayer()
            }
        }
        updateNotification()
    }

    private fun connectIfNeeded() {
        if (host.isBlank()) {
            lastError = "Host 为空"
            updateNotification()
            return
        }

        if (wsState != AbpWebSocketClient.State.DISCONNECTED) {
            updateNotification()
            return
        }

        val deviceId = getOrCreateDeviceId()
        Log.i(TAG, "Connecting: host=$host, uplink=$enableUplink, downlink=$enableDownlink, deviceId=$deviceId")

        wsClient.connect(
            host = host,
            token = token,
            deviceId = deviceId,
            callbacks = object : AbpWebSocketClient.Callbacks {
                override fun onState(state: AbpWebSocketClient.State) {
                    Log.i(TAG, "WS state=$state")
                    wsState = state
                    if (state == AbpWebSocketClient.State.DISCONNECTED) {
                        audioManager.stop()
                        releaseWakeLock()
                    }
                    updateNotification()
                }

                override fun onWelcome(welcome: WelcomeMessage) {
                    Log.i(TAG, "Welcome: codec=${welcome.selected.codec}, sr=${welcome.selected.sampleRate}")
                    lastError = null

                    // 收到 Welcome 后启动音频（按当前开关）
                    audioManager.start(enableUplink, enableDownlink)
                    acquireWakeLock()
                    updateNotification()
                }

                override fun onError(message: String) {
                    lastError = message
                    Log.e(TAG, "WS error: $message")
                    updateNotification()
                }

                override fun onLog(line: String) {
                    // 服务内不展示日志 UI；必要时可转存
                }

                override fun onDownlinkFrame(pcmPayload: ByteArray) {
                    if (enableDownlink) {
                        audioManager.writeDownlinkFrame(pcmPayload)
                    }
                }

                override fun onControlMessage(message: AbpControlMessage) {
                    // v1：暂不处理
                }
            },
        )
    }

    private fun disconnectAndStopSelf() {
        disconnectInternal()
        stopForegroundCompat()
        startedForeground = false
        stopSelf()
    }

    private fun disconnectInternal() {
        try {
            audioManager.stop()
        } catch (e: Exception) {
            Log.w(TAG, "audio stop error", e)
        }

        try {
            wsClient.disconnect()
        } catch (e: Exception) {
            Log.w(TAG, "ws disconnect error", e)
        }

        wsState = AbpWebSocketClient.State.DISCONNECTED
        releaseWakeLock()
    }

    private fun ensureForegroundStarted() {
        if (startedForeground) {
            updateNotification()
            return
        }

        val notification = buildNotification()

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(
                NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE or ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK,
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }

        startedForeground = true
        mainHandler.removeCallbacks(notificationTicker)
        mainHandler.post(notificationTicker)
    }

    private fun stopForegroundCompat() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
            stopForeground(STOP_FOREGROUND_REMOVE)
        } else {
            @Suppress("DEPRECATION")
            stopForeground(true)
        }
    }

    private fun updateNotification() {
        if (!startedForeground) return
        NotificationManagerCompat.from(this).notify(NOTIFICATION_ID, buildNotification())
    }

    private fun buildNotification(): Notification {
        val openIntent = Intent(this, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }

        val stopIntent = Intent(this, AudioBridgeForegroundService::class.java).apply {
            action = ACTION_STOP
        }

        val piFlags = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        } else {
            @Suppress("DEPRECATION")
            PendingIntent.FLAG_UPDATE_CURRENT
        }

        val pendingOpen = PendingIntent.getActivity(this, 0, openIntent, piFlags)
        val pendingStop = PendingIntent.getService(this, 1, stopIntent, piFlags)

        val stateText = when (wsState) {
            AbpWebSocketClient.State.CONNECTING -> "连接中…"
            AbpWebSocketClient.State.CONNECTED -> "已连接（codec=${wsClient.selectedCodec}）"
            AbpWebSocketClient.State.DISCONNECTED -> "未连接"
        }

        val trafficText = if (wsState == AbpWebSocketClient.State.CONNECTED) {
            val up = formatBytes(wsClient.uplinkPayloadBytesSentCount)
            val down = formatBytes(wsClient.downlinkPayloadBytesReceivedCount)
            "↑$up ↓$down"
        } else {
            ""
        }

        val detail = buildString {
            append(stateText)
            if (trafficText.isNotBlank()) {
                append(" | ").append(trafficText)
            }
            val err = lastError
            if (!err.isNullOrBlank()) {
                append(" | 错误: ").append(err)
            }
        }

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_launcher)
            .setContentTitle("AudioBridge 运行中")
            .setContentText(detail)
            .setContentIntent(pendingOpen)
            .setOngoing(true)
            .addAction(0, "停止", pendingStop)
            .build()
    }

    private fun formatBytes(bytes: Long): String {
        if (bytes < 0) return "-"
        if (bytes < 1024) return "${bytes}B"
        if (bytes < 1024 * 1024) return String.format("%.1fKB", bytes / 1024.0)
        if (bytes < 1024L * 1024 * 1024) return String.format("%.1fMB", bytes / (1024.0 * 1024.0))
        return String.format("%.2fGB", bytes / (1024.0 * 1024.0 * 1024.0))
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val mgr = getSystemService(NOTIFICATION_SERVICE) as NotificationManager

        val channel = NotificationChannel(
            CHANNEL_ID,
            "AudioBridge",
            NotificationManager.IMPORTANCE_LOW,
        ).apply {
            description = "AudioBridge 后台语音桥接"
        }

        mgr.createNotificationChannel(channel)
    }

    private fun acquireWakeLock() {
        if (wakeLock?.isHeld == true) return
        try {
            val pm = getSystemService(POWER_SERVICE) as PowerManager
            wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "AudioBridge:Voice").apply {
                setReferenceCounted(false)
                acquire() // 连接存续期间保持；断开/停止时释放
            }
        } catch (e: Exception) {
            Log.w(TAG, "acquire wake lock failed", e)
        }
    }

    private fun releaseWakeLock() {
        try {
            wakeLock?.let {
                if (it.isHeld) it.release()
            }
        } catch (e: Exception) {
            // ignore
        } finally {
            wakeLock = null
        }
    }

    private fun getOrCreateDeviceId(): String {
        val sp = getSharedPreferences("audiobridge", MODE_PRIVATE)
        val existing = sp.getString("deviceId", null)
        if (!existing.isNullOrBlank()) return existing
        val created = "android-" + UUID.randomUUID().toString()
        sp.edit().putString("deviceId", created).apply()
        return created
    }
}

