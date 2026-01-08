package com.audiobridge.client.ws

import com.audiobridge.client.abp.AbpControlJson
import com.audiobridge.client.abp.HelloCapabilities
import com.audiobridge.client.abp.HelloMessage
import com.audiobridge.client.abp.WelcomeMessage
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener

class AbpWebSocketClient(
    private val okHttpClient: OkHttpClient = OkHttpClient(),
) {
    enum class State {
        DISCONNECTED,
        CONNECTING,
        CONNECTED,
    }

    interface Callbacks {
        fun onState(state: State)
        fun onWelcome(welcome: WelcomeMessage)
        fun onError(message: String)
        fun onLog(line: String)
    }

    private var ws: WebSocket? = null
    private var state: State = State.DISCONNECTED

    fun connect(
        host: String,
        port: Int,
        token: String?,
        deviceId: String,
        callbacks: Callbacks,
    ) {
        if (state != State.DISCONNECTED) return
        setState(State.CONNECTING, callbacks)

        val url = "ws://$host:$port/abp"
        val request = Request.Builder().url(url).build()

        callbacks.onLog("Connecting $url ...")
        ws = okHttpClient.newWebSocket(
            request,
            object : WebSocketListener() {
                override fun onOpen(webSocket: WebSocket, response: Response) {
                    callbacks.onLog("WS opened: ${response.code}")
                    setState(State.CONNECTED, callbacks)

                    val hello = HelloMessage(
                        deviceId = deviceId,
                        token = token?.takeIf { it.isNotBlank() },
                        cap = HelloCapabilities(
                            codec = arrayOf("pcm"),
                            sampleRate = intArrayOf(48000),
                            frameMs = intArrayOf(20),
                            uplink = true,
                            downlink = true,
                        ),
                    )
                    webSocket.send(hello.toJson())
                }

                override fun onMessage(webSocket: WebSocket, text: String) {
                    callbacks.onLog("WS text: $text")
                    try {
                        val msg = AbpControlJson.parse(text)
                        if (msg is WelcomeMessage) {
                            callbacks.onWelcome(msg)
                        }
                    } catch (e: Exception) {
                        callbacks.onError("Parse control msg failed: ${e.message}")
                    }
                }

                override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                    callbacks.onError("WS failure: ${t.message}")
                    callbacks.onLog("WS response: ${response?.code}")
                    setState(State.DISCONNECTED, callbacks)
                }

                override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                    callbacks.onLog("WS closed: $code $reason")
                    setState(State.DISCONNECTED, callbacks)
                }
            },
        )
    }

    fun disconnect(callbacks: Callbacks) {
        ws?.close(1000, "bye")
        ws = null
        setState(State.DISCONNECTED, callbacks)
    }

    private fun setState(newState: State, callbacks: Callbacks) {
        state = newState
        callbacks.onState(newState)
    }
}

