using Client.Core.Decoding.Backends;
using Client.Core.Decoding.Renderers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SimpleVms.FFmpeg.Native;

namespace Client.Core.Decoding;

public sealed class DecodePipelineFactory
{
  private readonly ILoggerFactory _loggerFactory;
  private readonly ILogger<DecodePipelineFactory> _logger;
  private readonly IConfiguration? _configuration;

  public DecodePipelineFactory(ILoggerFactory loggerFactory, IConfiguration? configuration = null)
  {
    _loggerFactory = loggerFactory;
    _logger = loggerFactory.CreateLogger<DecodePipelineFactory>();
    _configuration = configuration;
  }

  public (IDecodeBackend Backend, IFrameRenderer Renderer)? Create(DecodeRole role)
  {
    var backend = SelectBackend(role);
    if (backend == null) return null;
    var renderer = new SkiaFrameRenderer(_loggerFactory.CreateLogger<SkiaFrameRenderer>());
    _logger.LogInformation("Selected decode path: {Backend} + {Renderer} (role={Role})",
      backend.DisplayName, renderer.DisplayName, role);
    return (backend, renderer);
  }

  private IDecodeBackend? SelectBackend(DecodeRole role)
  {
    var forced = _configuration?["OverrideDecoder"];
    var value = string.IsNullOrEmpty(forced) ? "auto" : forced.ToLowerInvariant();
    _logger.LogInformation("OverrideDecoder = {Value}", value);

    return value switch
    {
      "auto" => role == DecodeRole.Tile ? NewSw() : NewAuto(),
      "vulkan_sw" => NewStrictHwToSw(HwToSwDecodeBackend.VulkanOnly, "HW Vulkan Decode"),
      "platform_sw" => NewStrictHwToSw(HwToSwDecodeBackend.PlatformOnly, "HW Platform Decode"),
      "sw" => NewSw(),
      _ => LogUnknownAndReturnNull(forced!)
    };
  }

  private IDecodeBackend? LogUnknownAndReturnNull(string value)
  {
    _logger.LogError("Unknown OverrideDecoder value '{Value}'. Valid: auto, vulkan_sw, platform_sw, sw.", value);
    return null;
  }

  private SwDecodeBackend NewSw() =>
    new(_loggerFactory.CreateLogger<SwDecodeBackend>());

  private HwToSwDecodeBackend NewAuto() =>
    new(_loggerFactory.CreateLogger<HwToSwDecodeBackend>());

  private HwToSwDecodeBackend NewStrictHwToSw(AVHWDeviceType[] allowed, string strictLabel) =>
    new(_loggerFactory.CreateLogger<HwToSwDecodeBackend>(), allowed, strictHw: true, strictLabel);
}
