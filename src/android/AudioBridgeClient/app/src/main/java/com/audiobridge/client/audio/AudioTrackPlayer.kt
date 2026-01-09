package com.audiobridge.client.audio

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioManager
import android.media.AudioTrack
import android.os.Build
import android.os.Process
import android.util.Log
import java.util.concurrent.ConcurrentLinkedQueue
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicLong
import kotlin.concurrent.thread

/**
 * 音频播放器：使用 AudioTrack 播放下行 PCM 音频
 */
class AudioTrackPlayer {

    companion object {
        private const val TAG = "AudioTrackPlayer"
        /** 最大缓冲帧数（防止内存溢出）*/
        private const val MAX_BUFFER_FRAMES = 100 // 2 秒
        /** 预缓冲帧数（启动/重缓冲时先攒几帧，抵抗网络抖动） */
        private const val PREBUFFER_FRAMES = 4 // 80ms
    }

    private var audioTrack: AudioTrack? = null
    private val isPlaying = AtomicBoolean(false)
    private var playThread: Thread? = null

    // 帧缓冲队列
    private val frameQueue = ConcurrentLinkedQueue<ByteArray>()
    private val bufferedCount = AtomicInteger(0)

    // 统计
    private val framesPlayed = AtomicLong(0)
    private val framesDropped = AtomicLong(0)
    private val underruns = AtomicLong(0)

    /** 当播放出错时触发 */
    var onError: ((String) -> Unit)? = null

    /** 是否正在播放 */
    val isRunning: Boolean get() = isPlaying.get()

    /** 缓冲队列中的帧数 */
    val bufferedFrames: Int get() = bufferedCount.get()

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

        // 使用较大的缓冲区（提升抗抖动能力，代价是延迟略增）
        val actualBufferSize = maxOf(bufferSize, AudioConfig.BYTES_PER_FRAME * 8) // 160ms

        try {
            val builder = AudioTrack.Builder()
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
            
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                builder.setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY)
            }

            audioTrack = builder.build()

            if (audioTrack?.state != AudioTrack.STATE_INITIALIZED) {
                onError?.invoke("AudioTrack 初始化失败")
                audioTrack?.release()
                audioTrack = null
                return false
            }

            isPlaying.set(true)

            // 重置统计
            framesPlayed.set(0)
            framesDropped.set(0)
            underruns.set(0)
            frameQueue.clear()
            bufferedCount.set(0)

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
        bufferedCount.set(0)

        Log.i(TAG, "已停止播放")
    }

    /**
     * 写入 PCM 帧到播放队列
     */
    fun writeFrame(pcmFrame: ByteArray) {
        if (!isPlaying.get()) return

        // 防止缓冲区溢出
        while (bufferedCount.get() >= MAX_BUFFER_FRAMES) {
            val dropped = frameQueue.poll() // 丢弃最老的帧
            if (dropped != null) {
                bufferedCount.decrementAndGet()
                framesDropped.incrementAndGet()
            } else {
                break
            }
        }

        frameQueue.offer(pcmFrame)
        bufferedCount.incrementAndGet()
    }

    private fun playLoop() {
        val silenceFrame = ByteArray(AudioConfig.BYTES_PER_FRAME)
        var playbackStarted = false

        while (isPlaying.get()) {
            try {
                val track = audioTrack ?: break
                
                // 提高播放线程优先级，减少调度抖动导致的“卡卡”
                if (!playbackStarted) {
                    try {
                        Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO)
                    } catch (_: Exception) {
                        // ignore
                    }
                }

                // 预缓冲：先攒几帧再 play，减少起始/抖动时的欠载
                if (!playbackStarted) {
                    if (bufferedCount.get() < PREBUFFER_FRAMES) {
                        Thread.sleep(5)
                        continue
                    }
                    track.play()
                    playbackStarted = true
                }

                val frame = frameQueue.poll()
                if (frame != null) {
                    bufferedCount.decrementAndGet()
                }

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
