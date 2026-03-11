# ConsoleApp1 改进方案

## 项目概述
ConsoleApp1是数据采集与检测系统的子进程，负责USB1602数据采集卡的控制和数据采集，通过NetMQ与主进程通信。

## 设计约束
1. **全程不使用异步**：数据采集系统实时性要求高，异步会增加线程池调度开销
2. **无GC开销**：性能监控和日志系统不能产生垃圾回收
3. **不干扰数据采集**：改进措施不能影响数据采集的实时性和稳定性

## 一、通信可靠性增强方案（无异步版本）

### 问题分析
当前[`ConsoleApp1/Program.cs`](ConsoleApp1/Program.cs:156)中的`CheckHeartbeat()`方法仅记录警告，无自动恢复机制。

### 改进目标
1. 实现同步自动重连机制
2. 避免线程池调度开销
3. 保持通信的实时性

### 具体实现

#### 1. SyncConnectionManager 类
```csharp
// ConsoleApp1/Tools/SyncConnectionManager.cs
using NetMQ;
using NetMQ.Sockets;
using System;
using System.Text;
using System.Threading;

namespace ConsoleApp1.Tools
{
    /// <summary>
    /// 同步通信管理器（无异步版本）
    /// </summary>
    public class SyncConnectionManager : IDisposable
    {
        private DealerSocket _socket;
        private volatile bool _isConnected = false;
        private DateTime _lastHeartbeat;
        private readonly object _reconnectLock = new object();
        private bool _disposed = false;
        
        public bool IsConnected => _isConnected;
        public DateTime LastHeartbeat => _lastHeartbeat;
        
        /// <summary>
        /// 同步连接方法
        /// </summary>
        public bool ConnectSync(string address = "tcp://127.0.0.1:5555", string identity = "AD_PROCESS")
        {
            lock (_reconnectLock)
            {
                try
                {
                    // 关闭旧连接
                    _socket?.Dispose();
                    
                    // 创建新连接
                    _socket = new DealerSocket();
                    _socket.Options.Identity = Encoding.UTF8.GetBytes(identity);
                    _socket.Connect(address);
                    
                    // 发送READY消息
                    _socket.SendFrame("READY", false);
                    
                    _isConnected = true;
                    _lastHeartbeat = DateTime.Now;
                    return true;
                }
                catch (Exception)
                {
                    _isConnected = false;
                    return false;
                }
            }
        }
        
        /// <summary>
        /// 同步重连方法（指数退避）
        /// </summary>
        public bool ReconnectSync(int maxRetries = 3)
        {
            lock (_reconnectLock)
            {
                for (int i = 0; i < maxRetries; i++)
                {
                    if (ConnectSync())
                    {
                        return true;
                    }
                    
                    // 指数退避等待
                    if (i < maxRetries - 1)
                    {
                        Thread.Sleep((int)Math.Pow(2, i) * 100);
                    }
                }
                return false;
            }
        }
        
        /// <summary>
        /// 同步发送消息
        /// </summary>
        public bool SendFrameSync(string message)
        {
            if (!_isConnected || _socket == null) return false;
            
            try
            {
                _socket.SendFrame(message, false);
                return true;
            }
            catch (Exception)
            {
                _isConnected = false;
                return false;
            }
        }
        
        /// <summary>
        /// 同步接收消息（带超时）
        /// </summary>
        public bool TryReceiveFrameStringSync(TimeSpan timeout, out string message)
        {
            message = null;
            if (!_isConnected || _socket == null) return false;
            
            try
            {
                return _socket.TryReceiveFrameString(timeout, out message);
            }
            catch (Exception)
            {
                _isConnected = false;
                return false;
            }
        }
        
        /// <summary>
        /// 同步心跳检查
        /// </summary>
        public void CheckHeartbeatSync(int timeoutSeconds = 10)
        {
            if (!_isConnected) return;
            
            var diff = DateTime.Now - _lastHeartbeat;
            if (diff.TotalSeconds > timeoutSeconds)
            {
                // 尝试同步重连
                ReconnectSync();
            }
        }
        
        /// <summary>
        /// 更新心跳时间
        /// </summary>
        public void UpdateHeartbeat()
        {
            _lastHeartbeat = DateTime.Now;
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _socket?.Dispose();
        }
    }
}
```

#### 2. 修改 Program.cs
```csharp
// 在ConsoleApp1/Program.cs中的修改
static void CheckHeartbeat()
{
    var diff = DateTime.Now - lastHeartbeat;
    
    // 如果10秒没有收到主进程消息
    if (diff.TotalSeconds > 10)
    {
        AppLogger.LogWarning("超过10秒未收到主进程消息，尝试重连");
        
        // 同步重连逻辑
        try
        {
            socket.Dispose();
            socket = new DealerSocket();
            socket.Options.Identity = Encoding.UTF8.GetBytes("AD_PROCESS");
            socket.Connect("tcp://127.0.0.1:5555");
            socket.SendFrame("READY");
            lastHeartbeat = DateTime.Now;
            AppLogger.LogInfo("重连成功");
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"重连失败: {ex.Message}");
        }
    }
}
```

## 二、性能监控方案（无GC开销版本）

### 问题分析
当前系统缺乏性能指标监控，难以诊断性能问题。

### 改进目标
1. 实现零GC开销的性能监控
2. 使用值类型和预分配内存
3. 支持关键性能指标采集

### 具体实现

#### 1. LowOverheadPerformanceMonitor 类
```csharp
// ConsoleApp1/Tools/LowOverheadPerformanceMonitor.cs
using System;
using System.Threading;

namespace ConsoleApp1.Tools
{
    /// <summary>
    /// 性能指标结构体（值类型，避免GC）
    /// </summary>
    public struct PerformanceMetrics
    {
        public long TotalSamples;          // 总采样点数
        public long SamplesPerSecond;      // 每秒采样数
        public int ChannelQueueDepth;      // 通道队列深度
        public long ProcessingLatencyMs;   // 处理延迟（毫秒）
        public int ErrorCount;             // 错误计数
        public DateTime LastUpdateTime;    // 最后更新时间
        
        public override string ToString()
        {
            return $"采样率: {SamplesPerSecond}/s, 总采样: {TotalSamples}, 队列深度: {ChannelQueueDepth}, 延迟: {ProcessingLatencyMs}ms";
        }
    }
    
    /// <summary>
    /// 低开销性能监控器
    /// </summary>
    public class LowOverheadPerformanceMonitor : IDisposable
    {
        // 预分配的指标缓冲区（避免运行时分配）
        private readonly PerformanceMetrics[] _metricBuffer;
        private int _currentIndex = 0;
        private readonly object _syncLock = new object();
        private bool _disposed = false;
        
        // 使用Interlocked进行无锁计数
        private long _totalSamples = 0;
        private long _lastSecondSamples = 0;
        private DateTime _lastSecondTime = DateTime.Now;
        private int _currentQueueDepth = 0;
        private int _errorCount = 0;
        
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="bufferSize">指标缓冲区大小，默认1000条记录</param>
        public LowOverheadPerformanceMonitor(int bufferSize = 1000)
        {
            // 预分配内存，避免运行时分配
            _metricBuffer = new PerformanceMetrics[bufferSize];
            _lastSecondTime = DateTime.Now;
        }
        
        /// <summary>
        /// 记录采样数据（无GC版本）
        /// </summary>
        public void RecordSample(int samplesRead)
        {
            // 使用Interlocked避免锁开销
            Interlocked.Add(ref _totalSamples, samplesRead);
            Interlocked.Add(ref _lastSecondSamples, samplesRead);
            
            // 每秒计算一次速率（减少计算频率）
            var now = DateTime.Now;
            if ((now - _lastSecondTime).TotalSeconds >= 1.0)
            {
                UpdateMetrics(now);
            }
        }
        
        /// <summary>
        /// 更新队列深度
        /// </summary>
        public void UpdateQueueDepth(int depth)
        {
            Interlocked.Exchange(ref _currentQueueDepth, depth);
        }
        
        /// <summary>
        /// 记录错误
        /// </summary>
        public void RecordError()
        {
            Interlocked.Increment(ref _errorCount);
        }
        
        /// <summary>
        /// 更新性能指标（每秒调用一次）
        /// </summary>
        private void UpdateMetrics(DateTime now)
        {
            lock (_syncLock)
            {
                // 创建新的指标（栈分配，无GC）
                var metrics = new PerformanceMetrics
                {
                    TotalSamples = _totalSamples,
                    SamplesPerSecond = _lastSecondSamples,
                    ChannelQueueDepth = _currentQueueDepth,
                    ErrorCount = _errorCount,
                    LastUpdateTime = now
                };
                
                // 循环缓冲区，避免分配
                _metricBuffer[_currentIndex] = metrics;
                _currentIndex = (_currentIndex + 1) % _metricBuffer.Length;
                
                // 重置每秒计数器
                _lastSecondSamples = 0;
                _lastSecondTime = now;
            }
        }
        
        /// <summary>
        /// 获取当前性能指标（零分配）
        /// </summary>
        public PerformanceMetrics GetCurrentMetrics()
        {
            lock (_syncLock)
            {
                if (_currentIndex == 0 && _metricBuffer.Length > 0)
                    return _metricBuffer[_metricBuffer.Length - 1];
                if (_currentIndex > 0)
                    return _metricBuffer[_currentIndex - 1];
                return new PerformanceMetrics();
            }
        }
        
        /// <summary>
        /// 获取性能报告字符串
        /// </summary>
        public string GetPerformanceReport()
        {
            var metrics = GetCurrentMetrics();
            return metrics.ToString();
        }
        
        /// <summary>
        /// 重置所有计数器
        /// </summary>
        public void Reset()
        {
            lock (_syncLock)
            {
                _totalSamples = 0;
                _lastSecondSamples = 0;
                _currentQueueDepth = 0;
                _errorCount = 0;
                _lastSecondTime = DateTime.Now;
                _currentIndex = 0;
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // 无需特殊清理，所有资源都是托管资源
        }
    }
}
```

#### 2. 在 AD_Controlcs 中集成
```csharp
// 在ConsoleApp1/Service/AD_Controlcs.cs中的修改
public class AD_Controlcs
{
    // 添加性能监控器实例
    private readonly LowOverheadPerformanceMonitor _perfMonitor;
    
    public AD_Controlcs()
    {
        _perfMonitor = new LowOverheadPerformanceMonitor();
        // ... 其他初始化
    }
    
    // 在数据采集线程中记录性能
    private void ADWorkThread()
    {
        while (ADDataTest_RunFlag)
        {
            // ... 数据采集逻辑
            
            // 记录性能指标（无GC开销）
            if (samplesRead > 0)
            {
                _perfMonitor.RecordSample(samplesRead);
            }
            
            // 更新队列深度
            if (channel.Reader.TryPeek(out _))
            {
                // 估算队列深度（实际实现需要根据具体逻辑）
                _perfMonitor.UpdateQueueDepth(estimatedDepth);
            }
        }
    }
    
    // 添加性能报告命令支持
    public string GetPerformanceReport()
    {
        return _perfMonitor.GetPerformanceReport();
    }
    
    // 在错误处理中记录
    private void HandleError(string errorMessage)
    {
        _perfMonitor.RecordError();
        // ... 其他错误处理逻辑
    }
}
```

## 三、日志系统方案（低开销版本）

### 问题分析
当前仅使用`Debug.WriteLine`进行简单日志，缺乏结构化日志和持久化存储。

### 改进目标
1. 实现低开销的日志系统
2. 避免使用重量级日志框架
3. 支持可配置的日志级别

### 具体实现

#### 1. LowOverheadLogger 类
```csharp
// ConsoleApp1/Tools/LowOverheadLogger.cs
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace ConsoleApp1.Tools
{
    /// <summary>
    /// 低开销日志器
    /// </summary>
    public class LowOverheadLogger : IDisposable
    {
        private StreamWriter _writer;
        private readonly LogLevel _minLevel;
        private readonly object _lock = new object();
        private bool _disposed = false;
        private string _logFilePath;
        
        public enum LogLevel
        {
            Debug = 0,
            Info = 1,
            Warning = 2,
            Error = 3
        }
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public LowOverheadLogger(string logFilePath, LogLevel minLevel = LogLevel.Info)
        {
            _minLevel = minLevel;
            _logFilePath = logFilePath;
            
            InitializeWriter();
        }
        
        /// <summary>
        /// 初始化写入器
        /// </summary>
        private void InitializeWriter()
        {
            lock (_lock)
            {
                try
                {
                    // 确保日志目录存在
                    var logDir = Path.GetDirectoryName(_logFilePath);
                    if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }
                    
                    // 使用FileShare.ReadWrite允许其他进程读取
                    var fileStream = new FileStream(
                        _logFilePath, 
                        FileMode.Append, 
                        FileAccess.Write, 
                        FileShare.ReadWrite);
                        
                    _writer = new StreamWriter(fileStream, Encoding.UTF8)
                    {
                        AutoFlush = true  // 自动刷新，避免数据丢失
                    };
                    
                    LogInternal(LogLevel.Info, $"日志系统初始化完成，日志级别: {_minLevel}");
                }
                catch (Exception ex)
                {
                    // 日志初始化失败时使用控制台输出
                    Console.WriteLine($"日志初始化失败: {ex.Message}");
                    _writer = null;
                }
            }
        }
        
        /// <summary>
        /// 低开销日志方法
        /// </summary>
        public void Log(LogLevel level, string message)
        {
            if (level < _minLevel) return;
            
            LogInternal(level, message);
        }
        
        /// <summary>
        /// 内部日志方法
        /// </summary>
        private void LogInternal(LogLevel level, string message)
        {
            lock (_lock)
            {
                if (_disposed) return;
                
                try
                {
                    if (_writer == null)
                    {
                        // 如果写入器未初始化，使用控制台输出
                        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
                        return;
                    }
                    
                    // 简单的格式化，避免字符串连接开销
                    _writer.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    _writer.Write(" [");
                    _writer.Write(level.ToString().ToUpper());
                    _writer.Write("] ");
                    _writer.WriteLine(message);
                }
                catch
                {
                    // 日志失败时静默处理，不影响主流程
                    // 可以尝试重新初始化写入器
                    try
                    {
                        _writer?.Dispose();
                        InitializeWriter();
                    }
                    catch
                    {
                        // 重试也失败，则放弃
                    }
                }
            }
        }
        
        // 重载方法，避免params数组分配
        public void LogDebug(string message) => Log(LogLevel.Debug, message);
        public void LogInfo(string message) => Log(LogLevel.Info, message);
        public void LogWarning(string message) => Log(LogLevel.Warning, message);
        public void LogError(string message) => Log(LogLevel.Error, message);
        
        /// <summary>
        /// 带异常信息的日志
        /// </summary>
        public void LogError(string message, Exception ex)
        {
            Log(LogLevel.Error, $"{message} - {ex.GetType().Name}: {ex.Message}");
        }
        
        /// <summary>
        /// 切换日志文件
        /// </summary>
        public void SwitchLogFile(string newLogFilePath)
        {
            lock (_lock)
            {
                try
                {
                    _writer?.Dispose();
                    _logFilePath = newLogFilePath;
                    InitializeWriter();
                }
                catch (Exception ex)
                {
                    LogInternal(LogLevel.Error, $"切换日志文件失败: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 更改日志级别
        /// </summary>
        public void ChangeLogLevel(LogLevel newLevel)
        {
            lock (_lock)
            {
                _minLevel = newLevel;
                LogInternal(LogLevel.Info, $"日志级别已更改为: {newLevel}");
            }
        }
        
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                
                try
                {
                    LogInternal(LogLevel.Info, "日志系统关闭");
                    _writer?.Dispose();
                }
                catch
                {
                    // 忽略关闭时的异常
                }
            }
        }
    }
    
    /// <summary>
    /// 全局日志器实例
    /// </summary>
    public static class AppLogger
    {
        private static LowOverheadLogger _instance;
        private static readonly object _initLock = new object();
        
        /// <summary>
        /// 初始化日志系统
        /// </summary>
        public static void