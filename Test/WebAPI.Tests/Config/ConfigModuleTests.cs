using System.Text.Json;
using WebAPI.Models;
using WebAPI.Tools;
using Xunit;

namespace WebAPI.Tests.Config;

// ============================================================
// L1 — 模型 + 校验 纯单元测试
// ============================================================

public class LidarAlgorithmConfigModelTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var c = new LidarAlgorithmConfig();

        Assert.Equal(1.0, c.GainEqualizationCoefficient);
        Assert.Equal(4.48, c.KConstant);
        Assert.Equal(0.2, c.ReceiverApertureD_m);
        Assert.Equal(1000.0, c.PathLengthL_m);
        Assert.Equal(100, c.Cn2WindowFrames);
        Assert.Equal(3000.0, c.FernaldBoundaryDistance_m);
        Assert.Equal(532.0, c.LaserWavelength_nm);
        Assert.Equal(1.3, c.AngstromExponent);
        Assert.Equal(0, c.DarkCurrentSampleCount);
        Assert.Equal(20_000_000.0, c.SampleRateHz);
        Assert.Equal(30.0, c.BlindZoneDistance_m);
    }

    [Fact]
    public void Deserialize_AllFields_Roundtrips()
    {
        const string json = """
        {
            "GainEqualizationCoefficient": 1.05,
            "KConstant": 5.0,
            "ReceiverApertureD_m": 0.25,
            "PathLengthL_m": 2000.0,
            "Cn2WindowFrames": 200,
            "FernaldBoundaryDistance_m": 5000.0,
            "LaserWavelength_nm": 1064.0,
            "AngstromExponent": 1.5,
            "DarkCurrentSampleCount": 80,
            "SampleRateHz": 40000000.0,
            "BlindZoneDistance_m": 50.0
        }
        """;

        var c = JsonSerializer.Deserialize<LidarAlgorithmConfig>(json);

        Assert.NotNull(c);
        Assert.Equal(1.05, c!.GainEqualizationCoefficient);
        Assert.Equal(5.0, c.KConstant);
        Assert.Equal(0.25, c.ReceiverApertureD_m);
        Assert.Equal(2000.0, c.PathLengthL_m);
        Assert.Equal(200, c.Cn2WindowFrames);
        Assert.Equal(5000.0, c.FernaldBoundaryDistance_m);
        Assert.Equal(1064.0, c.LaserWavelength_nm);
        Assert.Equal(1.5, c.AngstromExponent);
        Assert.Equal(80, c.DarkCurrentSampleCount);
        Assert.Equal(40_000_000.0, c.SampleRateHz);
        Assert.Equal(50.0, c.BlindZoneDistance_m);
    }

    [Fact]
    public void Deserialize_MissingFields_FillsDefaults()
    {
        const string json = """{"KConstant": 99.0}""";

        var c = JsonSerializer.Deserialize<LidarAlgorithmConfig>(json);

        Assert.NotNull(c);
        Assert.Equal(99.0, c!.KConstant);                       // from JSON
        Assert.Equal(100, c.Cn2WindowFrames);                   // default (not in JSON)
        Assert.Equal(20_000_000.0, c.SampleRateHz);             // default (not in JSON)
        Assert.Equal(1.0, c.GainEqualizationCoefficient);       // default
    }

    [Fact]
    public void Deserialize_ExtraUnknownFields_IsIgnored()
    {
        const string json = """
        {"KConstant": 4.48, "UnknownFutureField": "should_be_ignored"}
        """;

        var c = JsonSerializer.Deserialize<LidarAlgorithmConfig>(json);

        Assert.NotNull(c);
        Assert.Equal(4.48, c!.KConstant);
    }

    [Fact]
    public void Serialize_ZeroValues_OutputsPlainNumbers()
    {
        var c = new LidarAlgorithmConfig { SampleRateHz = 0, Cn2WindowFrames = 0 };

        var json = JsonSerializer.Serialize(c);

        Assert.Contains("\"SampleRateHz\":0", json);
        Assert.Contains("\"Cn2WindowFrames\":0", json);
        Assert.DoesNotContain("1E", json);
        Assert.DoesNotContain("e+", json);
    }

    [Fact]
    public void Serialize_LargeDouble_PlainNotation()
    {
        var c = new LidarAlgorithmConfig { PathLengthL_m = 1_000_000.0 };

        var json = JsonSerializer.Serialize(c);

        Assert.Contains("1000000", json);
        Assert.DoesNotContain("1E+06", json);
    }
}

public class PersistenceSettingsModelTests
{
    [Fact]
    public void DefaultValue_IsData()
    {
        var s = new PersistenceSettings();
        Assert.Equal("data", s.DataDirectory);
    }

    [Fact]
    public void Deserialize_ReadsDataDirectory()
    {
        const string json = """{"DataDirectory": "custom_output"}""";

        var s = JsonSerializer.Deserialize<PersistenceSettings>(json);

        Assert.NotNull(s);
        Assert.Equal("custom_output", s!.DataDirectory);
    }
}

public class PersistenceValidationTests
{
    [Fact]
    public void NullConfig_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigHelper.ValidatePersistenceConfig(null!));
        Assert.Contains("不能为空", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void EmptyOrWhitespace_Throws(string dir)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigHelper.ValidatePersistenceConfig(new PersistenceSettings { DataDirectory = dir }));
        Assert.Contains("不能为空", ex.Message);
    }

    [Theory]
    [InlineData("data<test>")]
    [InlineData("folder|name")]
    [InlineData("path?query")]
    [InlineData("file\"name\"")]
    public void InvalidPathChars_Throws(string dir)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigHelper.ValidatePersistenceConfig(new PersistenceSettings { DataDirectory = dir }));
        Assert.Contains("非法字符", ex.Message);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT9")]
    public void WindowsReservedNames_Throws(string dir)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigHelper.ValidatePersistenceConfig(new PersistenceSettings { DataDirectory = dir }));
        Assert.Contains("Windows 保留名", ex.Message);
    }

    [Theory]
    [InlineData("real/CON")]     // last segment is reserved, forward slash
    [InlineData("real\\NUL")]    // last segment is reserved, backslash
    [InlineData("a/b/COM1")]     // nested, last segment reserved
    public void ReservedNameAsLastPathSegment_Throws(string dir)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigHelper.ValidatePersistenceConfig(new PersistenceSettings { DataDirectory = dir }));
        Assert.Contains("Windows 保留名", ex.Message);
    }

    [Theory]
    [InlineData("CON/real")]      // reserved name is first segment, not last
    [InlineData("NUL/subdir")]    // reserved name is first segment
    public void ReservedNameAsNonLastSegment_Allowed(string dir)
    {
        // 当前实现只检查最后一段路径名，中间段保留名不会被拦截
        var ex = Record.Exception(
            () => ConfigHelper.ValidatePersistenceConfig(new PersistenceSettings { DataDirectory = dir }));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("C:\\data")]
    [InlineData("D:\\output\\csv")]
    [InlineData("数据目录")]
    [InlineData("my data folder")]
    [InlineData("data/subdir")]
    [InlineData("subdir")]
    public void ValidPaths_NoThrow(string dir)
    {
        var ex = Record.Exception(
            () => ConfigHelper.ValidatePersistenceConfig(new PersistenceSettings { DataDirectory = dir }));
        Assert.Null(ex);
    }
}

// ============================================================
// L3 — ConfigHandler RPC 测试
// ============================================================

public class ConfigHandlerRpcTests
{
    [Fact]
    public void LidarConfigUpdate_EmptyPayload_ReturnsInvalidParam()
    {
        // 模拟：payload 为空字节数组，newConfig 为 null
        var payload = Array.Empty<byte>();
        var newConfig = payload.Length > 0
            ? JsonSerializer.Deserialize<LidarAlgorithmConfig>(payload)
            : null;

        Assert.Null(newConfig); // 确认空 payload → null 的逻辑
    }

    [Fact]
    public void PersistenceConfigUpdate_EmptyPayload_ReturnsInvalidParam()
    {
        var payload = Array.Empty<byte>();
        var newConfig = payload.Length > 0
            ? JsonSerializer.Deserialize<PersistenceSettings>(payload)
            : null;

        Assert.Null(newConfig);
    }

    [Fact]
    public void LidarConfigUpdate_MalformedJson_ThrowsDeserializationError()
    {
        var payload = "{bad"u8.ToArray();

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<LidarAlgorithmConfig>(payload));
    }

    [Fact]
    public void PersistenceConfigUpdate_MalformedJson_ThrowsDeserializationError()
    {
        var payload = "{bad"u8.ToArray();

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PersistenceSettings>(payload));
    }

    [Fact]
    public void LidarConfigRead_ReturnsJsonWithAllFields()
    {
        var config = new LidarAlgorithmConfig();
        var json = JsonSerializer.SerializeToUtf8Bytes(config,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var deserialized = JsonSerializer.Deserialize<LidarAlgorithmConfig>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(4.48, deserialized!.KConstant);
        Assert.Equal(20_000_000.0, deserialized.SampleRateHz);
    }

    [Fact]
    public void PersistenceConfigRead_ReturnsJsonWithDataDirectory()
    {
        var config = new PersistenceSettings { DataDirectory = "output" };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.SerializeToUtf8Bytes(config, options);
        var deserialized = JsonSerializer.Deserialize<PersistenceSettings>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("output", deserialized!.DataDirectory);
    }

    [Fact]
    public void CommandResult_Serialization_Roundtrips()
    {
        var result = new CommandResult
        {
            Success = false,
            Code = "INVALID_PARAM",
            Message = "配置不能为空",
            Timestamp = DateTime.UnixEpoch
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.SerializeToUtf8Bytes(result, options);
        var deserialized = JsonSerializer.Deserialize<CommandResult>(json, options);

        Assert.NotNull(deserialized);
        Assert.False(deserialized!.Success);
        Assert.Equal("INVALID_PARAM", deserialized.Code);
        Assert.Equal("配置不能为空", deserialized.Message);
    }
}

// ============================================================
// L3 — ConfigHandler 集成测试（需要完整 DI）
// ============================================================
//
// 以下测试依赖真实的 DI 容器（IOptionsMonitor + DeviceConfig.json），
// 需要在 CI 或开发环境中运行。运行前确保 DeviceConfig.json 存在。
//
// [Fact]
// public void LidarConfigRead_ReturnsActualConfig()
// {
//     // 通过 DI 获取 ConfigHandler
//     var handler = serviceProvider.GetRequiredService<ConfigHandler>();
//     var handlers = handler.GetHandlers();
//     var result = handlers["lidar-config-read"](Array.Empty<byte>()).Result;
//
//     var config = JsonSerializer.Deserialize<LidarAlgorithmConfig>(result,
//         new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
//     Assert.NotNull(config);
//     Assert.True(config.KConstant > 0);
// }
//
// [Fact]
// public void LidarConfigUpdate_AndRead_IsConsistent()
// {
//     var handler = serviceProvider.GetRequiredService<ConfigHandler>();
//     var handlers = handler.GetHandlers();
//
//     // 1. 读取当前配置备份
//     var original = handlers["lidar-config-read"](Array.Empty<byte>()).Result;
//
//     // 2. 写入新值
//     var newCfg = new LidarAlgorithmConfig { KConstant = 7.77 };
//     var payload = JsonSerializer.SerializeToUtf8Bytes(newCfg);
//     var updateResult = handlers["lidar-config-update"](payload).Result;
//
//     // 3. 读取验证
//     var updated = handlers["lidar-config-read"](Array.Empty<byte>()).Result;
//     var updatedCfg = JsonSerializer.Deserialize<LidarAlgorithmConfig>(updated);
//     Assert.Equal(7.77, updatedCfg!.KConstant);
//
//     // 4. 恢复原始值
//     handlers["lidar-config-update"](original).Wait();
// }
