namespace SpellCrafter.Data;

public sealed class EsoDataConnectionFactory : IEsoDataConnectionFactory
{
    public EsoDataConnection CreateConnection()
    {
        return new EsoDataConnection();
    }
}
