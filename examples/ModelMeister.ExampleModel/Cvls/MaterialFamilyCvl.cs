using ModelMeister.Model.Cvls;

namespace ModelMeister.ExampleModel.Cvls;

public enum MaterialFamily
{
    Paperboard,
    Aluminium,
    Polymer,
    Tin,
}

public sealed class MaterialFamilyCvl : CvlFromEnum<MaterialFamily>;
