namespace Utils;

public interface IObserverHarness<in T>
{
  void Block(bool coded);
  void Begin(T phase);
  void End(T phase);
}
