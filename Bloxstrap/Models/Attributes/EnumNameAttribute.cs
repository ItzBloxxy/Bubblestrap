namespace Bloxstrap.Models.Attributes
{
    class EnumNameAttribute : Attribute
    {
        public string? StaticName { get; set; }
        public string? FromTranslation { get; set; }
    }
}
