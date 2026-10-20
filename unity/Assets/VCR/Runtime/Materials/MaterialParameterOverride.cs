using System;

namespace VCR.Runtime.Materials
{
    [Serializable]
    public struct MaterialParameterOverride
    {
        public string Name;
        public ShaderParameterKind Kind;
        public float X;
        public float Y;
        public float Z;
        public float W;
        public int IntValue;
        public bool BoolValue;
        public string StringValue;

        public static MaterialParameterOverride Float(
            string name,
            float value)
        {
            return new MaterialParameterOverride
            {
                Name = name,
                Kind = ShaderParameterKind.Float,
                X = value
            };
        }

        public static MaterialParameterOverride Int(
            string name,
            int value)
        {
            return new MaterialParameterOverride
            {
                Name = name,
                Kind = ShaderParameterKind.Int,
                IntValue = value
            };
        }

        public static MaterialParameterOverride Bool(
            string name,
            bool value)
        {
            return new MaterialParameterOverride
            {
                Name = name,
                Kind = ShaderParameterKind.Bool,
                BoolValue = value
            };
        }

        public static MaterialParameterOverride Color(
            string name,
            float r,
            float g,
            float b,
            float a = 1f)
        {
            return new MaterialParameterOverride
            {
                Name = name,
                Kind = ShaderParameterKind.Color,
                X = r,
                Y = g,
                Z = b,
                W = a
            };
        }

        public static MaterialParameterOverride Vector(
            string name,
            float x,
            float y,
            float z,
            float w)
        {
            return new MaterialParameterOverride
            {
                Name = name,
                Kind = ShaderParameterKind.Vector,
                X = x,
                Y = y,
                Z = z,
                W = w
            };
        }
    }
}
