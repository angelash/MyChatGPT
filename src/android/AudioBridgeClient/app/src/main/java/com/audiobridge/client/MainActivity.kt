package com.audiobridge.client

import android.Manifest
import android.content.pm.PackageManager
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.widget.Button
import android.widget.EditText
import android.widget.Switch
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import com.audiobridge.client.abp.AbpControlMessage
import com.audiobridge.client.abp.WelcomeMessage
import com.audiobridge.client.audio.AudioBridgeManager
import com.audiobridge.client.ws.AbpWebSocketClient
import java.util.UUID

class MainActivity : AppCompatActivity() {

    private lateinit var statusText: TextView
    private lateinit var audioStatusText: TextView
    private lateinit var hostInput: EditText
    private lateinit var tokenInput: EditText
    private lateinit var connectButton: Button
    private lateinit var uplinkSwitch: Switch
    private lateinit var downlinkSwitch: Switch

    private val wsClient = AbpWebSocketClient()
    private val audioManager = AudioBridgeManager()
    private val handler = Handler(Looper.getMainLooper())
    private var statusUpdateRunnable: Runnable? = null

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

        // 上行开关变化时动态启停麦克风
        uplinkSwitch.setOnCheckedChangeListener { _, isChecked ->
            if (audioManager.running) {
                if (isChecked) {
                    audioManager.startCapture()
                } else {
                    audioManager.stopCapture()
                }
            }
        }

        // 设置音频回调
        audioManager.onUplinkFrame = { frame ->
            // 上行音频：麦克风 -> Windows
            wsClient.sendUplinkFrame(frame)
        }
        audioManager.onError = { msg ->
            runOnUiThread { statusText.text = "音频错误：$msg" }
        }

        connectButton.setOnClickListener {
            if (!wsClient.isConnected) {
                connect()
            } else {
                disconnect()
            }
        }
    }

    private fun connect() {
        val host = hostInput.text?.toString()?.trim().orEmpty()
        if (host.isBlank()) {
            statusText.text = "请填写服务器地址"
            return
        }

        val token = tokenInput.text?.toString()?.trim().orEmpty()
        val deviceId = getOrCreateDeviceId()

        wsClient.connect(
            host = host,
            token = token,
            deviceId = deviceId,
            callbacks = object : AbpWebSocketClient.Callbacks {
                override fun onState(state: AbpWebSocketClient.State) {
                    runOnUiThread {
                        connectButton.text = if (state == AbpWebSocketClient.State.CONNECTED) "断开" else "连接"
                        statusText.text = "状态：$state"

                        if (state == AbpWebSocketClient.State.DISCONNECTED) {
                            stopAudioAndStatusUpdate()
                        }
                    }
                }

                override fun onWelcome(welcome: WelcomeMessage) {
                    runOnUiThread {
                        statusText.text = "已连接：codec=${welcome.selected.codec}, sr=${welcome.selected.sampleRate}"
                        // 收到 Welcome 后启动音频
                        startAudioAndStatusUpdate()
                    }
                }

                override fun onError(message: String) {
                    runOnUiThread { statusText.text = "错误：$message" }
                }

                override fun onLog(line: String) {
                    // MVP：先不做日志面板
                }

                override fun onDownlinkFrame(pcmPayload: ByteArray) {
                    // 下行音频：Windows -> Android 播放
                    audioManager.writeDownlinkFrame(pcmPayload)
                }

                override fun onControlMessage(message: AbpControlMessage) {
                    // 处理其他控制消息
                }
            },
        )
    }

    private fun disconnect() {
        stopAudioAndStatusUpdate()
        wsClient.disconnect()
        connectButton.text = "连接"
        statusText.text = "未连接"
    }

    private fun startAudioAndStatusUpdate() {
        val enableUplink = uplinkSwitch.isChecked
        val enableDownlink = downlinkSwitch.isChecked

        if (enableUplink && !hasRecordAudioPermission()) {
            requestRecordAudioPermission()
            statusText.text = "需要麦克风权限"
            return
        }

        audioManager.start(enableUplink, enableDownlink)

        // 启动状态更新
        statusUpdateRunnable = object : Runnable {
            override fun run() {
                updateAudioStatus()
                handler.postDelayed(this, 500)
            }
        }
        handler.post(statusUpdateRunnable!!)
    }

    private fun stopAudioAndStatusUpdate() {
        statusUpdateRunnable?.let { handler.removeCallbacks(it) }
        statusUpdateRunnable = null
        audioManager.stop()
        audioStatusText.text = ""
    }

    private fun updateAudioStatus() {
        val status = buildString {
            appendLine("音频状态：")
            appendLine("  麦克风：${if (audioManager.isCaptureRunning) "✓" else "✗"}")
            appendLine("  播放器：${if (audioManager.isPlayerRunning) "✓" else "✗"}")
            appendLine("  上行帧：${audioManager.uplinkFrames}")
            appendLine("  下行帧：${audioManager.downlinkFrames}")
            appendLine("  播放缓冲：${audioManager.playerBufferedMs}ms")
            appendLine("  欠载：${audioManager.playerUnderrunCount}")
        }
        audioStatusText.text = status
    }

    private fun hasRecordAudioPermission(): Boolean {
        return ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) ==
            PackageManager.PERMISSION_GRANTED
    }

    private fun requestRecordAudioPermission() {
        ActivityCompat.requestPermissions(this, arrayOf(Manifest.permission.RECORD_AUDIO), 1001)
    }

    private fun getOrCreateDeviceId(): String {
        val sp = getSharedPreferences("audiobridge", MODE_PRIVATE)
        val existing = sp.getString("deviceId", null)
        if (!existing.isNullOrBlank()) return existing
        val created = "android-" + UUID.randomUUID().toString()
        sp.edit().putString("deviceId", created).apply()
        return created
    }

    override fun onDestroy() {
        super.onDestroy()
        stopAudioAndStatusUpdate()
        wsClient.disconnect()
    }
}
