package com.audiobridge.client

import android.Manifest
import android.content.pm.PackageManager
import android.os.Bundle
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import com.audiobridge.client.ws.AbpWebSocketClient
import java.util.UUID

class MainActivity : AppCompatActivity() {

    private lateinit var statusText: TextView
    private lateinit var hostInput: EditText
    private lateinit var portInput: EditText
    private lateinit var tokenInput: EditText
    private lateinit var connectButton: Button
    private val wsClient = AbpWebSocketClient()
    private var isConnected = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        statusText = findViewById(R.id.statusText)
        hostInput = findViewById(R.id.hostInput)
        portInput = findViewById(R.id.portInput)
        tokenInput = findViewById(R.id.tokenInput)
        connectButton = findViewById(R.id.connectButton)

        if (!hasRecordAudioPermission()) {
            requestRecordAudioPermission()
        }

        // 默认端口：与 Windows Agent 保持一致（可改）
        if (portInput.text.isNullOrBlank()) {
            portInput.setText("21347")
        }

        connectButton.setOnClickListener {
            val host = hostInput.text?.toString()?.trim().orEmpty()
            val port = portInput.text?.toString()?.trim().orEmpty()
            if (host.isBlank() || port.isBlank()) {
                statusText.text = "请先填写 Host/Port"
                return@setOnClickListener
            }

            val portInt = port.toIntOrNull()
            if (portInt == null || portInt <= 0 || portInt > 65535) {
                statusText.text = "端口不合法：$port"
                return@setOnClickListener
            }

            val token = tokenInput.text?.toString()?.trim().orEmpty()
            val deviceId = getOrCreateDeviceId()

            if (!isConnected) {
                wsClient.connect(
                    host = host,
                    port = portInt,
                    token = token,
                    deviceId = deviceId,
                    callbacks = object : AbpWebSocketClient.Callbacks {
                        override fun onState(state: AbpWebSocketClient.State) {
                            runOnUiThread {
                                isConnected = (state == AbpWebSocketClient.State.CONNECTED)
                                connectButton.text = if (isConnected) "Disconnect" else "Connect"
                                statusText.text = "状态：$state"
                            }
                        }

                        override fun onWelcome(welcome: com.audiobridge.client.abp.WelcomeMessage) {
                            runOnUiThread {
                                statusText.text = "已连接：codec=${welcome.selected.codec}, sr=${welcome.selected.sampleRate}"
                            }
                        }

                        override fun onError(message: String) {
                            runOnUiThread { statusText.text = "错误：$message" }
                        }

                        override fun onLog(line: String) {
                            // MVP：先不做日志面板
                        }
                    },
                )
            } else {
                wsClient.disconnect(object : AbpWebSocketClient.Callbacks {
                    override fun onState(state: AbpWebSocketClient.State) {}
                    override fun onWelcome(welcome: com.audiobridge.client.abp.WelcomeMessage) {}
                    override fun onError(message: String) {}
                    override fun onLog(line: String) {}
                })
                isConnected = false
                connectButton.text = "Connect"
                statusText.text = "未连接"
            }
        }
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
}

