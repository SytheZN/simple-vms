using Client.Core.ViewModels;
using Shared.Models;
using Shared.Models.Dto;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client.ViewModels;

[TestFixture]
public class TimelineViewModelTests
{
  /// <summary>
  /// SCENARIO:
  /// SetVisibleRange is called and the API returns timeline data
  ///
  /// ACTION:
  /// Configure the VM with a camera, set a visible range
  ///
  /// EXPECTED RESULT:
  /// Spans and Events collections are populated from the API response
  /// </summary>
  [Test]
  public async Task SetVisibleRange_LoadsTimeline()
  {
    var api = new TimelineApi
    {
      Response = new TimelineResponse
      {
        Spans = [
          new TimelineSpan { StartTime = 1_000_000, EndTime = 2_000_000 },
          new TimelineSpan { StartTime = 3_000_000, EndTime = 4_000_000 }
        ],
        Events = [
          new TimelineEvent { Id = Guid.NewGuid(), Type = "motion", StartTime = 1_500_000 }
        ]
      }
    };

    var vm = new TimelineViewModel(api);
    vm.Configure(Guid.NewGuid(), "main");
    vm.SetVisibleRange(1_000_000, 5_000_000);

    await Task.Delay(300);

    Assert.That(vm.Spans, Has.Count.EqualTo(2));
    Assert.That(vm.Events, Has.Count.EqualTo(1));
    Assert.That(vm.VisibleFrom, Is.EqualTo(1_000_000UL));
    Assert.That(vm.VisibleTo, Is.EqualTo(5_000_000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// CurrentPosition is set
  ///
  /// ACTION:
  /// Set CurrentPosition to a new value
  ///
  /// EXPECTED RESULT:
  /// PropertyChanged fires for CurrentPosition
  /// </summary>
  [Test]
  public void CurrentPosition_Set_FiresPropertyChanged()
  {
    var vm = new TimelineViewModel(new TimelineApi());

    var changed = new List<string>();
    vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

    vm.CurrentPosition = 3_000_000;

    Assert.That(changed, Does.Contain("CurrentPosition"));
    Assert.That(vm.CurrentPosition, Is.EqualTo(3_000_000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// ZoomLevel is changed
  ///
  /// ACTION:
  /// Configure the VM, set a visible range, then change ZoomLevel
  ///
  /// EXPECTED RESULT:
  /// PropertyChanged fires for ZoomLevel, timeline data is re-fetched
  /// </summary>
  [Test]
  public async Task ZoomLevel_Changed_RefetchesData()
  {
    var api = new TimelineApi
    {
      Response = new TimelineResponse { Spans = [], Events = [] }
    };

    var vm = new TimelineViewModel(api);
    vm.Configure(Guid.NewGuid(), "main");
    vm.SetVisibleRange(1_000_000, 5_000_000);

    await Task.Delay(300);
    var callsBefore = api.CallCount;

    vm.ZoomLevel = 2.0;
    await Task.Delay(300);

    Assert.That(api.CallCount, Is.GreaterThan(callsBefore));
  }

  /// <summary>
  /// SCENARIO:
  /// ZoomLevel is set below the minimum
  ///
  /// ACTION:
  /// Set ZoomLevel to 0.01
  ///
  /// EXPECTED RESULT:
  /// ZoomLevel is clamped to 0.1
  /// </summary>
  [Test]
  public void ZoomLevel_BelowMinimum_Clamped()
  {
    var vm = new TimelineViewModel(new TimelineApi());

    vm.ZoomLevel = 0.01;

    Assert.That(vm.ZoomLevel, Is.EqualTo(0.1).Within(0.001));
  }

  private sealed class TimelineApi : FakeApiClient
  {
    public TimelineResponse? Response { get; set; }
    public int CallCount { get; private set; }

    public override Task<OneOf<TimelineResponse, Error>> GetTimelineAsync(
      Guid cid, ulong f, ulong t, string? p, CancellationToken ct)
    {
      CallCount++;
      return Task.FromResult<OneOf<TimelineResponse, Error>>(
        Response ?? new TimelineResponse { Spans = [], Events = [] });
    }
  }
}
