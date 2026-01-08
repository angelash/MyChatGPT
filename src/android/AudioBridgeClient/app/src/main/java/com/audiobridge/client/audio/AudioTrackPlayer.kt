package com.audiobridge.client.audio

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioManager
import android.media.AudioTrack
import android.util.Log
import java.util.concurrent.ConcurrentLinkedQueue
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import kotlin.concurrent.thread

/**
 * 音频播放器：使用 AudioTrack 播放下行 PCM 音频
 */
class AudioTrackPlayer {

    companion object {
        private const val TAG = "AudioTrackPlayer"
        /** 最大缓冲帧数（防止内存溢出）*/
        private const val MAX_BUFFER_FRAMES = 50 // 1 秒
    }

    private var audioTrack: AudioTrack? = null
    private val isPlaying = AtomicBoolean(false)
    private var playThread: Thread? = null

    // 帧缓冲队列
    private val frameQueue = ConcurrentLinkedQueue<ByteArray>()

    // 统计
    private val framesPlayed = AtomicLong(0)
    private val framesDropped = AtomicLong(0)
    private val underruns = AtomicLong(0)

    /** 当播放出错时触发 */
    var onError: ((String) -> Unit)? = null

    /** 是否正在播放 */
    val isRunning: Boolean get() = isPlaying.get()

    /** 缓冲队列中的帧数 */
    val bufferedFrames: Int get() = frameQueue.size

    /** 缓冲时长（毫秒）*/
    val bufferedMs: Int get() = bufferedFrames * AudioConfig.FRAME_MS

    /** 已播放帧数 */
    val playedFrames: Long get() = framesPlayed.get()

    /** 丢弃帧数 */
    val droppedFrames: Long get() = framesDropped.get()

    /** 欠载次数 */
    val underrunCount: Long get() = underruns.get()

    /**
     * 开始播放
     */
    fun start(): Boolean {
        if (isPlaying.get()) {
            Log.w(TAG, "Already playing")
            return true
        }

        val bufferSize = AudioTrack.getMinBufferSize(
            AudioConfig.SAMPLE_RATE,
            AudioFormat.CHANNEL_OUT_MONO,
            AudioFormat.ENCODING_PCM_16BIT
        )

        if (bufferSize == AudioTrack.ERROR_BAD_VALUE || bufferSize == AudioTrack.ERROR) {
            onError?.invoke("无法获取合适的缓冲区大小")
            return false
        }

        // 使用较大的缓冲区
        val actualBufferSize = maxOf(bufferSize, AudioConfig.BYTES_PER_FRAME * 4)

        try {
            audioTrack = AudioTrack.Builder()
                .setAudioAttributes(
                    AudioAttributes.Builder()
                        .setUsage(AudioAttributes.USAGE_MEDIA)
                        .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                        .build()
                )
                .setAudioFormat(
                    AudioFormat.Builder()
                        .setSampleRate(AudioConfig.SAMPLE_RATE)
                        .setChannelMask(AudioFormat.CHANNEL_OUT_MONO)
                        .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                        .build()
                )
                .setBufferSizeInBytes(actualBufferSize)
                .setTransferMode(AudioTrack.MODE_STREAM)
                .build()

            if (audioTrack?.state != AudioTrack.STATE_INITIALIZED) {
                onError?.invoke("AudioTrack 初始化失败")
                audioTrack?.release()
                audioTrack = null
                return false
            }

            audioTrack?.play()
            isPlaying.set(true)

            // 重置统计
            framesPlayed.set(0)
            framesDropped.set(0)
            underruns.set(0)
            frameQueue.clear()

            playThread = thread(name = "AudioTrackPlayer") {
                playLoop()
            }

            Log.i(TAG, "开始播放：bufferSize=$actualBufferSize")
            return true
        } catch (e: Exception) {
            Log.e(TAG, "启动播放失败", e)
            onError?.invoke("启动播放失败：${e.message}")
            audioTrack?.release()
            audioTrack = null
            return false
        }
    }

    /**
     * 停止播放
     */
    fun stop() {
        if (!isPlaying.get()) return

        isPlaying.set(false)
        playThread?.interrupt()
        playThread = null

        try {
            audioTrack?.stop()
        } catch (e: Exception) {
            Log.w(TAG, "停止播放异常", e)
        }

        audioTrack?.release()
        audioTrack = null
        frameQueue.clear()

        Log.i(TAG, "已停止播放")
    }

    /**
     * 写入 PCM 帧到播放队列
     */
    fun writeFrame(pcmFrame: ByteArray) {
        if (!isPlaying.get()) return

        // 防止缓冲区溢出
        if (frameQueue.size >= MAX_BUFFER_FRAMES) {
            frameQueue.poll() // 丢弃最老的帧
            framesDropped.incrementAndGet()
        }

        frameQueue.offer(pcmFrame)
    }

    private fun playLoop() {
        val silenceFrame = ByteArray(AudioConfig.BYTES_PER_FRAME)

        while (isPlaying.get()) {
            try {
                val track = audioTrack ?: break
                val frame = frameQueue.poll()

                if (frame != null) {
                    val written = track.write(frame, 0, frame.size)
                    if (written > 0) {
                        framesPlayed.incrementAndGet()
                    } else if (written == AudioTrack.ERROR_INVALID_OPERATION) {
                        Log.e(TAG, "AudioTrack 无效操作")
                        onError?.invoke("播放无效操作")
                        break
                    } else if (written == AudioTrack.ERROR_BAD_VALUE) {
                        Log.e(TAG, "AudioTrack 参数错误")
                        onError?.invoke("播放参数错误")
                        break
                    }
                } else {
                    // 缓冲区空，播放静音以保持流畅
                    underruns.incrementAndGet()
                    track.write(silenceFrame, 0, silenceFrame.size)

                    // 短暂休眠减少 CPU 占用
                    Thread.sleep(AudioConfig.FRAME_MS.toLong() / 2)
                }
            } catch (e: InterruptedException) {
                Log.i(TAG, "播放线程被中断")
                break
            } catch (e: Exception) {
                Log.e(TAG, "播放异常", e)
                onError?.invoke("播放异常：${e.message}")
                break
            }
        }

        Log.i(TAG, "播放循环结束")
    }
}
