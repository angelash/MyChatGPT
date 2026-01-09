package com.audiobridge.client

import android.Manifest
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.widget.Button
import android.widget.EditText
import android.widget.Switch
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import com.audiobridge.client.service.AudioBridgeForegroundService

class MainActivity : AppCompatActivity() {

    private lateinit var statusText: TextView
    private lateinit var audioStatusText: TextView
    private lateinit var hostInput: EditText
    private lateinit var tokenInput: EditText
    private lateinit var connectButton: Button
    private lateinit var uplinkSwitch: Switch
    private lateinit var downlinkSwitch: Switch

    private val handler = Handler(Looper.getMainLooper())
    private var statusUpdateRunnable: Runnable? = null

    private var service: AudioBridgeForegroundService? = null
    private var serviceBound: Boolean = false

    private var pendingStartAfterPermission: Boolean = false

    private val serviceConnection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, binder: IBinder?) {
            val b = binder as? AudioBridgeForegroundService.LocalBinder
            service = b?.getService()
            serviceBound = service != null
            updateUiOnce()
            startStatusUpdate()
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            serviceBound = false
            service = null
            stopStatusUpdate()
            updateUiOnce()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        statusText = findViewById(R.id.statusText)
        audioStatusText = findViewById(R.id.audioStatusText)
        hostInput = findViewById(R.id.hostInput)
        tokenInput = findViewById(R.id.tokenInput)
        connectButton = findViewById(R.id.connectButton)
        uplinkSwitch = findViewById(R.id.uplinkSwitch)
        downlinkSwitch = findViewById(R.id.downlinkSwitch)

        if (!hasRecordAudioPermission()) {
            requestRecordAudioPermission()
        }

        // 默认启用上下行
        uplinkSwitch.isChecked = true
        downlinkSwitch.isChecked = true

        // 上行开关变化时动态启停麦克风（服务模式下直接下发到 Service）
        uplinkSwitch.setOnCheckedChangeListener { _, isChecked ->
            service?.setEnableUplink(isChecked)
        }

        // 下行开关：当前版本下发给 Service（如果要动态启停播放器，后续可扩展）
        downlinkSwitch.setOnCheckedChangeListener { _, isChecked ->
            service?.setEnableDownlink(isChecked)
        }

        connectButton.setOnClickListener {
            val snap = service?.getSnapshot()
            val connected = snap?.wsState == com.audiobridge.client.ws.AbpWebSocketClient.State.CONNECTED
            val connecting = snap?.wsState == com.audiobridge.client.ws.AbpWebSocketClient.State.CONNECTING

            if (!connected && !connecting) {
                connect()
            } else {
                disconnect()
            }
        }
    }

    override fun onStart() {
        super.onStart()
        // 绑定服务（不一定已前台运行；仅用于 UI 获取状态/下发控制）
        bindService(Intent(this, AudioBridgeForegroundService::class.java), serviceConnection, Context.BIND_AUTO_CREATE)
    }

    override fun onStop() {
        super.onStop()
        stopStatusUpdate()
        if (serviceBound) {
            unbindService(serviceConnection)
            serviceBound = false
            service = null
        }
    }

    private fun connect() {
        val host = hostInput.text?.toString()?.trim().orEmpty()
        if (host.isBlank()) {
            statusText.text = "请填写服务器地址"
            return
        }

        val token = tokenInput.text?.toString()?.trim().orEmpty()
        val enableUplink = uplinkSwitch.isChecked
        val enableDownlink = downlinkSwitch.isChecked

        if (enableUplink && !hasRecordAudioPermission()) {
            requestRecordAudioPermission()
            statusText.text = "需要麦克风权限"
            pendingStartAfterPermission = true
            return
        }

        if (!hasPostNotificationsPermission()) {
            requestPostNotificationsPermission()
            statusText.text = "需要通知权限以在后台保持运行"
            pendingStartAfterPermission = true
            return
        }

        pendingStartAfterPermission = false
        startForegroundBridgeService(host, token, enableUplink, enableDownlink)
        startStatusUpdate()
    }

    private fun disconnect() {
        // 用户“手动断开”= 停止前台服务，并关闭自动重连
        pendingStartAfterPermission = false
        stopStatusUpdate()
        try {
            service?.requestStop()
        } catch (_: Exception) {
            // ignore
        }

        // 兜底：即使未绑定也尝试 stopService
        try {
            stopService(Intent(this, AudioBridgeForegroundService::class.java))
        } catch (_: Exception) {
            // ignore
        }

        // 解除绑定，确保服务可以真正销毁（否则 bind-only 会继续存活）
        if (serviceBound) {
            try {
                unbindService(serviceConnection)
            } catch (_: Exception) {
                // ignore
            } finally {
                serviceBound = false
                service = null
            }
        }
        updateUiOnce()
    }

    private fun startStatusUpdate() {
        if (statusUpdateRunnable != null) return
        // 启动状态更新（仅 UI）
        statusUpdateRunnable = object : Runnable {
            override fun run() {
                updateUiOnce()
                handler.postDelayed(this, 500)
            }
        }
        statusUpdateRunnable?.let { handler.post(it) }
    }

    private fun stopStatusUpdate() {
        statusUpdateRunnable?.let { handler.removeCallbacks(it) }
        statusUpdateRunnable = null
    }

    private fun updateUiOnce() {
        val snap = service?.getSnapshot()

        if (snap == null) {
            connectButton.text = "连接"
            statusText.text = "未连接"
            audioStatusText.text = ""
            return
        }

        connectButton.text = if (snap.wsState == com.audiobridge.client.ws.AbpWebSocketClient.State.CONNECTED) "断开" else "连接"
        statusText.text = "状态：${snap.wsState}"

        val status = buildString {
            appendLine("音频状态：")
            appendLine("  协商 codec：${snap.selectedCodec}")
            appendLine("  上行开关：${if (snap.enableUplink) "✓" else "✗"}")
            appendLine("  下行开关：${if (snap.enableDownlink) "✓" else "✗"}")
            appendLine("  麦克风：${if (snap.captureRunning) "✓" else "✗"}")
            appendLine("  播放器：${if (snap.playerRunning) "✓" else "✗"}")
            appendLine("  上行捕获帧：${snap.uplinkFramesCaptured}")
            appendLine("  上行发送帧：${snap.uplinkFramesSent}（静音丢弃 ${snap.uplinkFramesSuppressed}）")
            appendLine("  上行发送字节：${formatBytes(snap.uplinkBytesSent)}")
            appendLine("  下行播放帧：${snap.downlinkFramesPlayed}")
            appendLine("  下行接收帧：${snap.downlinkFramesReceived}")
            appendLine("  下行接收字节：${formatBytes(snap.downlinkBytesReceived)}")
            appendLine("  播放缓冲：${snap.playerBufferedMs}ms")
            appendLine("  欠载：${snap.playerUnderrunCount}")
            if (!snap.lastError.isNullOrBlank()) {
                appendLine("  错误：${snap.lastError}")
            }
        }

        audioStatusText.text = status
    }

    private fun formatBytes(bytes: Long): String {
        if (bytes < 0) return "-"
        if (bytes < 1024) return "${bytes}B"
        if (bytes < 1024 * 1024) return String.format("%.1fKB", bytes / 1024.0)
        if (bytes < 1024L * 1024 * 1024) return String.format("%.1fMB", bytes / (1024.0 * 1024.0))
        return String.format("%.2fGB", bytes / (1024.0 * 1024.0 * 1024.0))
    }

    private fun hasRecordAudioPermission(): Boolean {
        return ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) ==
            PackageManager.PERMISSION_GRANTED
    }

    private fun requestRecordAudioPermission() {
        ActivityCompat.requestPermissions(this, arrayOf(Manifest.permission.RECORD_AUDIO), 1001)
    }

    private fun hasPostNotificationsPermission(): Boolean {
        if (Build.VERSION.SDK_INT < 33) return true
        return ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) ==
            PackageManager.PERMISSION_GRANTED
    }

    private fun requestPostNotificationsPermission() {
        if (Build.VERSION.SDK_INT < 33) return
        ActivityCompat.requestPermissions(this, arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1002)
    }

    private fun startForegroundBridgeService(host: String, token: String, enableUplink: Boolean, enableDownlink: Boolean) {
        val i = Intent(this, AudioBridgeForegroundService::class.java).apply {
            action = AudioBridgeForegroundService.ACTION_START
            putExtra(AudioBridgeForegroundService.EXTRA_HOST, host)
            putExtra(AudioBridgeForegroundService.EXTRA_TOKEN, token)
            putExtra(AudioBridgeForegroundService.EXTRA_ENABLE_UPLINK, enableUplink)
            putExtra(AudioBridgeForegroundService.EXTRA_ENABLE_DOWNLINK, enableDownlink)
        }

        // Android 8+：必须用 startForegroundService
        ContextCompat.startForegroundService(this, i)
    }

    override fun onDestroy() {
        super.onDestroy()
        stopStatusUpdate()
    }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)

        if (!pendingStartAfterPermission) return

        if (requestCode == 1001 || requestCode == 1002) {
            val enableUplink = uplinkSwitch.isChecked
            val micOk = !enableUplink || hasRecordAudioPermission()
            val notifOk = hasPostNotificationsPermission()

            if (micOk && notifOk) {
                pendingStartAfterPermission = false
                connect()
            } else {
                pendingStartAfterPermission = false
            }
        }
    }
}
