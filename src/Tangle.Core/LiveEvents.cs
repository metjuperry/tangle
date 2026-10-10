namespace Tangle.Core;

public interface ILiveEvents
{
    void Publish(string type, object data);
}
