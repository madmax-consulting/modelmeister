using ModelMeister.Model;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.EntityTypes;

/// <summary>
/// Plain entity used as the source of an entity-backed CVL (see <see cref="Cvls.ChannelsCvl"/>) and as
/// the target of a non-link-entity link type. Exercises EntityTypeDescription + Icon + Settings on the
/// entity type itself.
/// </summary>
public sealed class Channel : EntityType
{
    public Channel()
    {
        EntityTypeName = new LocaleString("Sales channel")
            .With("en-US", "Sales channel")
            .With("sv-SE", "Försäljningskanal");
        EntityTypeDescription = new LocaleString("Routing target for syndication output");
        Icon = "channel.svg";
        Settings["BackOfficeOnly"] = "true";
    }

    [DisplayName, Unique]
    public Field<string> ChannelCode { get; init; } = new();

    [DisplayDescription]
    public Field<LocaleString> ChannelName { get; init; } = new();
}
