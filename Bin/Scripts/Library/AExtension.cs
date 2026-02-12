namespace AExtension
{
    public struct LengthVector(float w, float h)
    {
        public readonly float W = w, H = h, OneX = w * 0.05625f, OneY = h * 0.6f;
    }
}

