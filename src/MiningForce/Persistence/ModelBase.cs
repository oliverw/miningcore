namespace MiningForce.Persistence
{
    public interface IModelBase<TIdentity>
    {
        TIdentity Id { get; set; }
    }
}
