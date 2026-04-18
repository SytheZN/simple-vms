using Client.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Dto;
using Shared.Protocol;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client.ViewModels;

[TestFixture]
public class EventsViewModelExtraTests
{
  /// <summary>
  /// SCENARIO:
  /// Real-time event arrives with a Type that does not match FilterType
  ///
  /// ACTION:
  /// Set FilterType = "motion", fire a "status" event
  ///
  /// EXPECTED RESULT:
  /// Event is filtered out
  /// </summary>
  [Test]
  public void RealtimeEvent_WrongType_Filtered()
  {
    var events = new FakeEventService();
    var vm = new EventsViewModel(new EventsApi(), events,
      NullLogger<EventsViewModel>.Instance);
    vm.FilterType = "motion";

    events.Fire(new EventChannelMessage
    {
      Id = Guid.NewGuid(), CameraId = Guid.NewGuid(),
      Type = "status", StartTime = 0
    }, EventChannelFlags.Start);

    Assert.That(vm.Events, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// FilterCameraId matches the incoming event camera id
  ///
  /// ACTION:
  /// Set FilterCameraId, fire matching event
  ///
  /// EXPECTED RESULT:
  /// Event is added to the list
  /// </summary>
  [Test]
  public void RealtimeEvent_MatchingCameraFilter_Added()
  {
    var events = new FakeEventService();
    var vm = new EventsViewModel(new EventsApi(), events,
      NullLogger<EventsViewModel>.Instance);
    var cam = Guid.NewGuid();
    vm.FilterCameraId = cam;

    events.Fire(new EventChannelMessage
    {
      Id = Guid.NewGuid(), CameraId = cam, Type = "motion", StartTime = 0
    }, EventChannelFlags.Start);

    Assert.That(vm.Events, Has.Count.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// API returns an error during page fetch
  ///
  /// ACTION:
  /// LoadAsync against an API that returns Error
  ///
  /// EXPECTED RESULT:
  /// ErrorMessage is populated; Events stays empty
  /// </summary>
  [Test]
  public async Task Load_ApiError_SetsError()
  {
    var api = new EventsApi { ReturnError = true };
    var vm = new EventsViewModel(api, new FakeEventService(),
      NullLogger<EventsViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(vm.ErrorMessage, Is.EqualTo("events fail"));
      Assert.That(vm.Events, Is.Empty);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// PrevPageAsync is called when Offset is already 0
  ///
  /// ACTION:
  /// Call PrevPageAsync without prior NextPageAsync
  ///
  /// EXPECTED RESULT:
  /// Offset stays at 0 (clamped); fetch is still issued
  /// </summary>
  [Test]
  public async Task PrevPage_AtStart_ClampsOffsetToZero()
  {
    var api = new EventsApi { EventList = [] };
    var vm = new EventsViewModel(api, new FakeEventService(),
      NullLogger<EventsViewModel>.Instance);

    await vm.PrevPageAsync(CancellationToken.None);

    Assert.That(vm.Offset, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// HasMore is true when API returns a full page
  ///
  /// ACTION:
  /// Configure API to return exactly 100 events, LoadAsync
  ///
  /// EXPECTED RESULT:
  /// HasMore is true (suggesting another page may exist)
  /// </summary>
  [Test]
  public async Task HasMore_TrueWhenFullPage()
  {
    var page = Enumerable.Range(0, 100)
      .Select(i => new EventDto
      {
        Id = Guid.NewGuid(), CameraId = Guid.NewGuid(),
        Type = "motion", StartTime = (ulong)i
      })
      .ToList();
    var api = new EventsApi { EventList = page };
    var vm = new EventsViewModel(api, new FakeEventService(),
      NullLogger<EventsViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);

    Assert.That(vm.HasMore, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// HasMore is false when API returns a partial page (fewer than the limit)
  ///
  /// ACTION:
  /// Configure API to return 5 events, LoadAsync
  ///
  /// EXPECTED RESULT:
  /// HasMore is false
  /// </summary>
  [Test]
  public async Task HasMore_FalseWhenPartialPage()
  {
    var page = Enumerable.Range(0, 5)
      .Select(i => new EventDto
      {
        Id = Guid.NewGuid(), CameraId = Guid.NewGuid(),
        Type = "motion", StartTime = (ulong)i
      })
      .ToList();
    var api = new EventsApi { EventList = page };
    var vm = new EventsViewModel(api, new FakeEventService(),
      NullLogger<EventsViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);

    Assert.That(vm.HasMore, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// Dispose detaches the event-service handler
  ///
  /// ACTION:
  /// Dispose, fire an event afterwards
  ///
  /// EXPECTED RESULT:
  /// Events stays empty; the handler did not fire
  /// </summary>
  [Test]
  public void Dispose_DetachesEventHandler()
  {
    var events = new FakeEventService();
    var vm = new EventsViewModel(new EventsApi(), events,
      NullLogger<EventsViewModel>.Instance);

    vm.Dispose();
    events.Fire(new EventChannelMessage
    {
      Id = Guid.NewGuid(), CameraId = Guid.NewGuid(),
      Type = "motion", StartTime = 0
    }, EventChannelFlags.Start);

    Assert.That(vm.Events, Is.Empty);
  }

  private sealed class EventsApi : FakeApiClient
  {
    public List<EventDto>? EventList { get; set; }
    public bool ReturnError { get; set; }

    public override Task<OneOf<IReadOnlyList<EventDto>, Error>> GetEventsAsync(
      Guid? cid, string? ty, ulong? f, ulong? t, int l, int o, CancellationToken ct)
    {
      if (ReturnError)
        return Task.FromResult(
          OneOf<IReadOnlyList<EventDto>, Error>.FromT1(
            new Error(Result.Unavailable, default, "events fail")));
      return Task.FromResult(
        OneOf<IReadOnlyList<EventDto>, Error>.FromT0((EventList ?? []).ToList()));
    }
  }
}
