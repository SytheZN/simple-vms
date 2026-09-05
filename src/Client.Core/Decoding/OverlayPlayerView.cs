namespace Client.Core.Decoding;

public readonly record struct OverlayPlayerView(
  long TimestampUs, double Rate, int Direction, bool Paused, Player.PlayerMode Mode);
