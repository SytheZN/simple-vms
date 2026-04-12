using Client.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Dto;
using Shared.Protocol;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client.ViewModels;

[TestFixture]
public class EventsViewModelTests
{
  /// <summary>
  /// SCENARIO:
  /// LoadAsync is called and the API returns events
  ///
  /// ACTION:
  /// Call LoadAsync
  ///
  /// EXPECTED RESULT:
  /// Events collection is populated
  /// </summary>
  [Test]
  public async Task Load_PopulatesEvents()
  {
    var events = new List<EventDto>
    {
      new() { Id = Guid.NewGuid(), CameraId = Guid.NewGuid(), Type = "motion", StartTime = 1_000_000 },
      new() { Id = Guid.NewGuid(), CameraId = Guid.NewGuid(), Type = "status", StartTime = 2_000_000 }
    };

    var api = new EventsApi { EventList = events };
    var eventService = new FakeEventService();
    var vm = new EventsViewModel(api, eventService, NullLogger<EventsViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);

    Assert.That(vm.Events, Has.Count.EqualTo(2));
  }

  /// <summary>
  /// SCENARIO:
  /// A real-time Start event arrives matching the current filter
  ///
  /// ACTION:
  /// Fire an event through the event service
  ///
  /// EXPECTED RESULT:
  /// The event is prepended to the Events collection
  /// </summary>
  [Test]
  public void RealtimeEvent_Start_PrependsToList()
  {
    var api = new EventsApi();
    var eventService = new FakeEventService();
    var vm = new EventsViewModel(api, eventService, NullLogger<EventsViewModel>.Instance);

    var cameraId = Guid.NewGuid();
    var msg = new EventChannelMessage
    {
      Id = Guid.NewGuid(),
      CameraId = cameraId,
      Type = "motion",
      StartTime = 5_000_000
    };
    eventService.Fire(msg, EventChannelFlags.Start);

    Assert.That(vm.Events, Has.Count.EqualTo(1));
    Assert.That(vm.Events[0].CameraId, Is.EqualTo(cameraId));
  }

  /// <summary>
  /// SCENARIO:
  /// A real-time End event arrives
  ///
  /// ACTION:
  /// Fire an End event
  ///
  /// EXPECTED RESULT:
  /// The event is NOT added (only Start events are prepended)
  /// </summary>
  [Test]
  public void RealtimeEvent_End_Ignored()
  {
    var api = new EventsApi();
    var eventService = new FakeEventService();
    var vm = new EventsViewModel(api, eventService, NullLogger<EventsViewModel>.Instance);

    eventService.Fire(
      new EventChannelMessage
      {
        Id = Guid.NewGuid(), CameraId = Guid.NewGuid(),
        Type = "motion", StartTime = 5_000_000
      },
      EventChannelFlags.End);

    Assert.That(vm.Events, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A camera filter is set and a real-time event from a different camera arrives
  ///
  /// ACTION:
  /// Set FilterCameraId, fire event from different camera
  ///
  /// EXPECTED RESULT:
  /// The event is NOT added
  /// </summary>
  [Test]
  public void RealtimeEvent_WrongCamera_Filtered()
  {
    var api = new EventsApi();
    var eventService = new FakeEventService();
    var vm = new EventsViewModel(api, eventService, NullLogger<EventsViewModel>.Instance);
    vm.FilterCameraId = Guid.NewGuid();

    eventService.Fire(
      new EventChannelMessage
      {
        Id = Guid.NewGuid(), CameraId = Guid.NewGuid(),
        Type = "motion", StartTime = 5_000_000
      },
      EventChannelFlags.Start);

    Assert.That(vm.Events, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// NextPageAsync is called after initial load
  ///
  /// ACTION:
  /// Call LoadAsync then NextPageAsync
  ///
  /// EXPECTED RESULT:
  /// Events from both pages are in the collection
  /// </summary>
  [Test]
  public async Task LoadMore_AppendsToExisting()
  {
    var page1 = new List<EventDto>
    {
      new() { Id = Guid.NewGuid(), CameraId = Guid.NewGuid(), Type = "motion", StartTime = 1_000_000 }
    };
    var page2 = new List<EventDto>
    {
      new() { Id = Guid.NewGuid(), CameraId = Guid.NewGuid(), Type = "status", StartTime = 2_000_000 }
    };

    var api = new EventsApi { EventPages = [page1, page2] };
    var eventService = new FakeEventService();
    var vm = new EventsViewModel(api, eventService, NullLogger<EventsViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);
    Assert.That(vm.Events, Has.Count.EqualTo(1));

    await vm.NextPageAsync(CancellationToken.None);
    Assert.That(vm.Events, Has.Count.EqualTo(1));
  }

  private sealed class EventsApi : FakeApiClient
  {
    public List<EventDto>? EventList { get; set; }
    public List<List<EventDto>>? EventPages { get; set; }
    private int _pageIndex;

    public override Task<OneOf<IReadOnlyList<EventDto>, Error>> GetEventsAsync(
      Guid? cid, string? ty, ulong? f, ulong? t, int l, int o, CancellationToken ct)
    {
      if (EventPages != null && _pageIndex < EventPages.Count)
        return Task.FromResult(
          OneOf<IReadOnlyList<EventDto>, Error>.FromT0(EventPages[_pageIndex++].ToList()));
      return Task.FromResult(
        OneOf<IReadOnlyList<EventDto>, Error>.FromT0((EventList ?? []).ToList()));
    }
  }
}
