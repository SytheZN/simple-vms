using Server.Core.Services;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Routing;

public static class ApiRoutes
{
  public static void Register(ApiDispatcher dispatcher)
  {
    RegisterEnrollment(dispatcher);
    RegisterCameras(dispatcher);
    RegisterClients(dispatcher);
    RegisterDiscovery(dispatcher);
    RegisterRecordings(dispatcher);
    RegisterEvents(dispatcher);
    RegisterRetention(dispatcher);
    RegisterSystem(dispatcher);
    RegisterPlugins(dispatcher);
  }

  private static void RegisterEnrollment(ApiDispatcher dispatcher)
  {
    dispatcher.Add("POST", "/api/v1/clients/enroll", (req, _) =>
    {
      var enrollment = req.Resolve<EnrollmentService>();
      var result = enrollment.StartEnrollment();
      return Task.FromResult(
        ApiResult.Created(result, new DebugTag(ModuleIds.Enrollment, 0x0010),
          ServerJsonContext.Default.StartEnrollmentResponse));
    });

    dispatcher.Add("GET", "/api/v1/clients/enroll/{token}/hold", async (req, ct) =>
    {
      var enrollment = req.Resolve<EnrollmentService>();
      var result = await enrollment.HoldTokenAsync(req.RouteString("token"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.Enrollment, 0x0012));
    });

    dispatcher.Add("POST", "/api/v1/enroll", async (req, ct) =>
    {
      var enrollment = req.Resolve<EnrollmentService>();
      var body = req.Body(ServerJsonContext.Default.EnrollRequest);
      var result = await enrollment.CompleteEnrollmentAsync(body.Token, ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.Enrollment, 0x0011),
        ServerJsonContext.Default.EnrollResponse);
    });
  }

  private static void RegisterCameras(ApiDispatcher dispatcher)
  {
    dispatcher.Add("GET", "/api/v1/cameras", async (req, ct) =>
    {
      var cameras = req.Resolve<CameraService>();
      var result = await cameras.GetAllAsync(req.QueryString("status"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0010),
        ServerJsonContext.Default.IReadOnlyListCameraListItem);
    });

    dispatcher.Add("POST", "/api/v1/cameras", async (req, ct) =>
    {
      var cameras = req.Resolve<CameraService>();
      var result = await cameras.CreateAsync(
        req.Body(ServerJsonContext.Default.CreateCameraRequest), ct);
      return ApiResult.Created(result, new DebugTag(ModuleIds.CameraManagement, 0x0011),
        ServerJsonContext.Default.CameraListItem);
    });

    dispatcher.Add("POST", "/api/v1/cameras/probe", async (req, ct) =>
    {
      var cameras = req.Resolve<CameraService>();
      var result = await cameras.ProbeAsync(
        req.Body(ServerJsonContext.Default.ProbeRequest), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0016),
        ServerJsonContext.Default.ProbeResponse);
    });

    dispatcher.Add("GET", "/api/v1/cameras/{id:guid}", async (req, ct) =>
    {
      var cameras = req.Resolve<CameraService>();
      var result = await cameras.GetByIdAsync(req.RouteGuid("id"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0012),
        ServerJsonContext.Default.CameraListItem);
    });

    dispatcher.Add("PUT", "/api/v1/cameras/{id:guid}", async (req, ct) =>
    {
      var cameras = req.Resolve<CameraService>();
      var result = await cameras.UpdateAsync(
        req.RouteGuid("id"), req.Body(ServerJsonContext.Default.UpdateCameraRequest), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0013),
        ServerJsonContext.Default.CameraListItem);
    });

    dispatcher.Add("DELETE", "/api/v1/cameras/{id:guid}", async (req, ct) =>
    {
      var cameras = req.Resolve<CameraService>();
      var result = await cameras.DeleteAsync(req.RouteGuid("id"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0014));
    });

    dispatcher.Add("POST", "/api/v1/cameras/{id:guid}/refresh", async (req, ct) =>
    {
      var cameras = req.Resolve<CameraService>();
      var result = await cameras.RefreshAsync(req.RouteGuid("id"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0017),
        ServerJsonContext.Default.CameraListItem);
    });

    dispatcher.Add("POST", "/api/v1/cameras/{id:guid}/restart", async (req, ct) =>
    {
      var cameras = req.Resolve<CameraService>();
      var result = await cameras.RestartAsync(req.RouteGuid("id"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0015));
    });

    dispatcher.Add("OPTIONS", "/api/v1/cameras/{id:guid}/config", async (req, ct) =>
    {
      var config = req.Resolve<CameraConfigService>();
      var result = await config.GetSchemaAsync(req.RouteGuid("id"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0020),
        ServerJsonContext.Default.CameraConfigSchema);
    });

    dispatcher.Add("GET", "/api/v1/cameras/{id:guid}/config", async (req, ct) =>
    {
      var config = req.Resolve<CameraConfigService>();
      var result = await config.GetValuesAsync(req.RouteGuid("id"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0021),
        ServerJsonContext.Default.CameraConfigValues);
    });

    dispatcher.Add("PUT", "/api/v1/cameras/{id:guid}/config", async (req, ct) =>
    {
      var config = req.Resolve<CameraConfigService>();
      var body = req.Body(ServerJsonContext.Default.CameraConfigValues);
      var result = await config.ApplyAsync(req.RouteGuid("id"), body, ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0022));
    });
  }

  private static void RegisterClients(ApiDispatcher dispatcher)
  {
    dispatcher.Add("GET", "/api/v1/clients", async (req, ct) =>
    {
      var clients = req.Resolve<ClientService>();
      var result = await clients.GetAllAsync(ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.ClientManagement, 0x0010),
        ServerJsonContext.Default.IReadOnlyListClientListItem);
    });

    dispatcher.Add("GET", "/api/v1/clients/{id:guid}", async (req, ct) =>
    {
      var clients = req.Resolve<ClientService>();
      var result = await clients.GetByIdAsync(req.RouteGuid("id"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.ClientManagement, 0x0011),
        ServerJsonContext.Default.ClientListItem);
    });

    dispatcher.Add("PUT", "/api/v1/clients/{id:guid}", async (req, ct) =>
    {
      var clients = req.Resolve<ClientService>();
      var result = await clients.UpdateAsync(
        req.RouteGuid("id"), req.Body(ServerJsonContext.Default.UpdateClientRequest), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.ClientManagement, 0x0012));
    });

    dispatcher.Add("DELETE", "/api/v1/clients/{id:guid}", async (req, ct) =>
    {
      var clients = req.Resolve<ClientService>();
      var result = await clients.RevokeAsync(req.RouteGuid("id"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.ClientManagement, 0x0013));
    });
  }

  private static void RegisterDiscovery(ApiDispatcher dispatcher)
  {
    dispatcher.Add("POST", "/api/v1/discovery", async (req, ct) =>
    {
      var discovery = req.Resolve<DiscoveryService>();
      var result = await discovery.DiscoverAsync(
        req.Body(ServerJsonContext.Default.DiscoveryRequest), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.Discovery, 0x0010),
        ServerJsonContext.Default.IReadOnlyListDiscoveredCameraDto);
    });
  }

  private static void RegisterRecordings(ApiDispatcher dispatcher)
  {
    dispatcher.Add("GET", "/api/v1/recordings/{cameraId:guid}", async (req, ct) =>
    {
      var recordings = req.Resolve<RecordingService>();
      var result = await recordings.GetSegmentsAsync(
        req.RouteGuid("cameraId"),
        req.QueryString("profile") ?? "main",
        req.QueryULong("from"),
        req.QueryULong("to"),
        ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.Recording, 0x0010),
        ServerJsonContext.Default.IReadOnlyListRecordingSegmentDto);
    });

    dispatcher.Add("GET", "/api/v1/recordings/{cameraId:guid}/timeline", async (req, ct) =>
    {
      var recordings = req.Resolve<RecordingService>();
      var result = await recordings.GetTimelineAsync(
        req.RouteGuid("cameraId"),
        req.QueryString("profile") ?? "main",
        req.QueryULong("from"),
        req.QueryULong("to"),
        ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.Recording, 0x0011),
        ServerJsonContext.Default.TimelineResponse);
    });
  }

  private static void RegisterEvents(ApiDispatcher dispatcher)
  {
    dispatcher.Add("GET", "/api/v1/events", async (req, ct) =>
    {
      var events = req.Resolve<EventService>();
      var result = await events.QueryAsync(
        req.QueryGuidOrNull("cameraId"),
        req.QueryString("type"),
        req.QueryULong("from"),
        req.QueryULong("to"),
        req.QueryIntOrDefault("limit", 100),
        req.QueryIntOrDefault("offset", 0),
        ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.Events, 0x0010),
        ServerJsonContext.Default.IReadOnlyListEventDto);
    });

    dispatcher.Add("GET", "/api/v1/events/{id:guid}", async (req, ct) =>
    {
      var events = req.Resolve<EventService>();
      var result = await events.GetByIdAsync(req.RouteGuid("id"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.Events, 0x0011),
        ServerJsonContext.Default.EventDto);
    });
  }

  private static void RegisterRetention(ApiDispatcher dispatcher)
  {
    dispatcher.Add("GET", "/api/v1/retention", async (req, ct) =>
    {
      var retention = req.Resolve<RetentionService>();
      var result = await retention.GetGlobalAsync(ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.Retention, 0x0010),
        ServerJsonContext.Default.RetentionPolicy);
    });

    dispatcher.Add("PUT", "/api/v1/retention", async (req, ct) =>
    {
      var retention = req.Resolve<RetentionService>();
      var result = await retention.SetGlobalAsync(
        req.Body(ServerJsonContext.Default.RetentionPolicy), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.Retention, 0x0011));
    });
  }

  private static void RegisterSystem(ApiDispatcher dispatcher)
  {
    dispatcher.Add("GET", "/api/v1/system/health", async (req, ct) =>
    {
      var system = req.Resolve<SystemService>();
      var health = await system.GetHealthAsync(ct);
      return ApiResult.Success(health, new DebugTag(ModuleIds.SystemManagement, 0x0010),
        ServerJsonContext.Default.HealthResponse);
    });

    dispatcher.Add("GET", "/api/v1/system/storage", async (req, ct) =>
    {
      var system = req.Resolve<SystemService>();
      var result = await system.GetStorageAsync(ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.SystemManagement, 0x0011),
        ServerJsonContext.Default.StorageResponse);
    });

    dispatcher.Add("GET", "/api/v1/system/settings", async (req, ct) =>
    {
      var system = req.Resolve<SystemService>();
      var result = await system.GetSettingsAsync(ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.SystemManagement, 0x0012),
        ServerJsonContext.Default.ServerSettings);
    });

    dispatcher.Add("PUT", "/api/v1/system/settings", async (req, ct) =>
    {
      var system = req.Resolve<SystemService>();
      var result = await system.UpdateSettingsAsync(
        req.Body(ServerJsonContext.Default.ServerSettings), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.SystemManagement, 0x0013));
    });

    dispatcher.Add("GET", "/api/v1/system/verify-remote-address", async (req, ct) =>
    {
      var system = req.Resolve<SystemService>();
      var result = await system.VerifyRemoteAddressAsync(req.QueryString("host"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.SystemManagement, 0x0017),
        ServerJsonContext.Default.VerifyRemoteAddressResponse);
    });

    dispatcher.Add("POST", "/api/v1/system/certs", (req, _) =>
    {
      var certs = req.Resolve<ICertificateService>();
      if (certs.HasCerts)
        return Task.FromResult(ApiResult.Err(
          Result.Conflict,
          new DebugTag(ModuleIds.SystemManagement, 0x0014),
          "Certificates already exist. To regenerate, delete the existing certificates first."));

      certs.GenerateCerts();
      return Task.FromResult(
        ApiResult.Success(new DebugTag(ModuleIds.SystemManagement, 0x0015)));
    });
  }

  private static void RegisterPlugins(ApiDispatcher dispatcher)
  {
    dispatcher.Add("GET", "/api/v1/plugins", (req, _) =>
    {
      var plugins = req.Resolve<PluginService>();
      var result = plugins.GetAll(req.QueryString("type"));
      return Task.FromResult(
        ApiResult.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0010),
          ServerJsonContext.Default.IReadOnlyListPluginListItem));
    });

    dispatcher.Add("GET", "/api/v1/plugins/{id}", (req, _) =>
    {
      var plugins = req.Resolve<PluginService>();
      var result = plugins.GetById(req.RouteString("id"));
      return Task.FromResult(
        ApiResult.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0011),
          ServerJsonContext.Default.PluginListItem));
    });

    dispatcher.Add("OPTIONS", "/api/v1/plugins/{id}/config", (req, _) =>
    {
      var plugins = req.Resolve<PluginService>();
      var result = plugins.GetConfigSchema(req.RouteString("id"));
      return Task.FromResult(
        ApiResult.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0014),
          ServerJsonContext.Default.IReadOnlyListSettingGroup));
    });

    dispatcher.Add("GET", "/api/v1/plugins/{id}/config", (req, _) =>
    {
      var plugins = req.Resolve<PluginService>();
      var result = plugins.GetConfigValues(req.RouteString("id"));
      return Task.FromResult(
        ApiResult.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0015),
          ServerJsonContext.Default.IReadOnlyDictionaryStringString));
    });

    dispatcher.Add("PUT", "/api/v1/plugins/{id}/config", (req, _) =>
    {
      var plugins = req.Resolve<PluginService>();
      var values = req.Body(ServerJsonContext.Default.DictionaryStringString);
      var result = plugins.ApplyConfigValues(req.RouteString("id"), values);
      return Task.FromResult(
        ApiResult.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0016)));
    });

    dispatcher.Add("POST", "/api/v1/plugins/{id}/config/validate", (req, _) =>
    {
      var plugins = req.Resolve<PluginService>();
      var body = req.Body(ServerJsonContext.Default.ValidateFieldRequest);
      var result = plugins.ValidateField(req.RouteString("id"), body.Key, body.Value);
      return Task.FromResult(
        ApiResult.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0017)));
    });

    dispatcher.Add("POST", "/api/v1/plugins/{id}/start", async (req, ct) =>
    {
      var plugins = req.Resolve<PluginService>();
      var result = await plugins.UserStartAsync(req.RouteString("id"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0012));
    });

    dispatcher.Add("POST", "/api/v1/plugins/{id}/stop", async (req, ct) =>
    {
      var plugins = req.Resolve<PluginService>();
      var result = await plugins.UserStopAsync(req.RouteString("id"), ct);
      return ApiResult.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0013));
    });
  }
}
