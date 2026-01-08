package com.audiobridge.client.audio

import android.util.Log
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

/**
 * 音频桥接管理器：协调麦克风捕获和音频播放
 */
class AudioBridgeManager {

    companion object {
        private const val TAG = "AudioBridgeManager"
    }

    private val capture = AudioRecordCapture()
    private val player = AudioTrackPlayer()
    private val isRunning = AtomicBoolean(false)

    // 上行帧计数
    private val uplinkFrameCount = AtomicLong(0)
    // 下行帧计数
    private val downlinkFrameCount = AtomicLong(0)

    /** 上行帧回调（麦克风 -> Windows）*/
    var onUplinkFrame: ((ByteArray) -> Unit)? = null

    /** 错误回调 */
    var onError: ((String) -> Unit)? = null

    /** 是否正在运行 */
    val running: Boolean get() = isRunning.get()

    /** 麦克风捕获是否运行 */
    val isCaptureRunning: Boolean get() = capture.isRunning

    /** 音频播放是否运行 */
    val isPlayerRunning: Boolean get() = player.isRunning

    /** 播放缓冲时长（毫秒）*/
    val playerBufferedMs: Int get() = player.bufferedMs

    /** 上行帧数 */
    val uplinkFrames: Long get() = uplinkFrameCount.get()

    /** 下行帧数 */
    val downlinkFrames: Long get() = downlinkFrameCount.get()

    /** 播放欠载次数 */
    val playerUnderrunCount: Long get() = player.underrunCount

    /**
     * 启动音频桥接
     * @param enableUplink 是否启用上行（麦克风）
     * @param enableDownlink 是否启用下行（播放）
     */
    fun start(enableUplink: Boolean = true, enableDownlink: Boolean = true): Boolean {
        if (isRunning.get()) {
            Log.w(TAG, "Already running")
            return true
        }

        uplinkFrameCount.set(0)
        downlinkFrameCount.set(0)

        var success = true

        // 启动上行（麦克风捕获）
        if (enableUplink) {
            capture.onFrameAvailable = { frame ->
                uplinkFrameCount.incrementAndGet()
                onUplinkFrame?.invoke(frame)
            }
            capture.onError = { msg ->
                onError?.invoke("上行错误：$msg")
            }

            if (!capture.start()) {
                success = false
                Log.e(TAG, "启动麦克风捕获失败")
            }
        }

        // 启动下行（音频播放）
        if (enableDownlink) {
            player.onError = { msg ->
                onError?.invoke("下行错误：$msg")
            }

            if (!player.start()) {
                success = false
                Log.e(TAG, "启动音频播放失败")
            }
        }

        isRunning.set(success)
        Log.i(TAG, "启动完成：uplink=$enableUplink, downlink=$enableDownlink, success=$success")
        return success
    }

    /**
     * 停止音频桥接
     */
    fun stop() {
        if (!isRunning.get()) return

        capture.stop()
        player.stop()
        isRunning.set(false)

        Log.i(TAG, "已停止")
    }

    /**
     * 写入下行音频帧（从 Windows 收到的系统声音）
     */
    fun writeDownlinkFrame(pcmFrame: ByteArray) {
        if (!isRunning.get()) return
        downlinkFrameCount.incrementAndGet()
        player.writeFrame(pcmFrame)
    }

    /**
     * 获取状态摘要
     */
    fun getStatusSummary(): String {
        return buildString {
            appendLine("音频桥接：${if (running) "运行中" else "已停止"}")
            appendLine("  - 麦克风：${if (isCaptureRunning) "✓" else "✗"}")
            appendLine("  - 播放器：${if (isPlayerRunning) "✓" else "✗"}")
            appendLine("  - 上行帧：$uplinkFrames")
            appendLine("  - 下行帧：$downlinkFrames")
            appendLine("  - 播放缓冲：${playerBufferedMs}ms")
            appendLine("  - 欠载次数：$playerUnderrunCount")
        }
    }
}
