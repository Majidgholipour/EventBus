namespace EventBus.InegrationEvent;

public static class Utility
{
    public static string GetEnumTitle(this Enum value)
    {
        return Enum.GetName(value.GetType(), value);
    }
}
