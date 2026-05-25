using System.Xml.Serialization;

namespace ToolCupboard
{
    /// <summary>
    /// A chat message with a configurable colour. Serializes as:
    /// <c>&lt;FieldName Text="..." Color="white" /&gt;</c>
    /// Color accepts names (white, red, green, yellow, ...) or hex (#RRGGBB).
    /// Placeholders such as {count} or {type} are substituted at send time.
    /// </summary>
    public sealed class Message
    {
        [XmlAttribute]
        public string Text;

        [XmlAttribute]
        public string Color;

        public Message() { }

        public Message(string text, string color)
        {
            Text = text;
            Color = color;
        }
    }
}
