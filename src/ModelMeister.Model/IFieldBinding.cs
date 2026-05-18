namespace ModelMeister.Model;

/// <summary>
/// Marker interface for types that can occupy the second/third type-parameter slot of
/// <see cref="Field{TData, TBinding}"/> and <see cref="Field{TData, TCvl, TCategory}"/>.
/// Implemented by <see cref="Cvl"/> and <see cref="Category"/> so a field can declare its
/// CVL or Category binding in the type system rather than via an explicit
/// <c>Category = typeof(...)</c> / <c>Cvl = typeof(...)</c> initializer.
/// </summary>
public interface IFieldBinding
{
}
