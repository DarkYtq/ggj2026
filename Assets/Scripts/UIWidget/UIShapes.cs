using UnityEngine;

/// <summary>
/// 运行时生成的占位精灵 + 系统中文字体。之后把各 Image 的 sprite 换成正式美术即可。
/// </summary>
public static class UIShapes
{
    private static Font _font;

    /// <summary>系统中文字体（macOS 上优先苹方/黑体，Win 上雅黑），保证中文不显示为方块</summary>
    public static Font CJKFont
    {
        get
        {
            if (_font == null)
            {
                string[] names = {
                    "PingFang SC", "Heiti SC", "Hiragino Sans GB",
                    "Microsoft YaHei", "SimHei", "Arial Unicode MS", "Arial"
                };
                _font = Font.CreateDynamicFontFromOSFont(names, 40);
            }
            return _font;
        }
    }

    // ---------- 精灵生成 ----------

    public static Sprite SolidCircle(int size, Color color)
    {
        var t = NewTex(size);
        float r = size * 0.5f, cx = r, cy = r;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                float a = Mathf.Clamp01(r - d);
                t.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * a));
            }
        return Finish(t);
    }

    /// <summary>占位小猫脸：黑色圆头 + 两只耳朵 + 大眼睛 + 白胸</summary>
    public static Sprite CatFace(int size)
    {
        var t = NewTex(size);
        Color clear = new Color(0, 0, 0, 0);
        Color black = new Color(0.13f, 0.13f, 0.16f, 1f);
        Color white = new Color(0.95f, 0.95f, 0.95f, 1f);
        Color gold = new Color(1f, 0.82f, 0.3f, 1f);

        for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                float x = (px + 0.5f) / size;   // 0..1
                float y = (py + 0.5f) / size;   // 0..1 (下->上)
                Color c = clear;

                // 耳朵（两个三角）
                if (InTri(x, y, 0.20f, 0.72f, 0.30f, 0.98f, 0.44f, 0.74f) ||
                    InTri(x, y, 0.80f, 0.72f, 0.70f, 0.98f, 0.56f, 0.74f))
                    c = black;

                // 头（椭圆）
                if (InEllipse(x, y, 0.5f, 0.45f, 0.40f, 0.40f)) c = black;

                if (c.a > 0f)
                {
                    // 白胸（下方三角）
                    if (InTri(x, y, 0.40f, 0.06f, 0.60f, 0.06f, 0.5f, 0.26f)) c = white;
                    // 眼睛（金色）+ 瞳孔（黑）
                    if (InEllipse(x, y, 0.38f, 0.48f, 0.075f, 0.10f) ||
                        InEllipse(x, y, 0.62f, 0.48f, 0.075f, 0.10f)) c = gold;
                    if (InEllipse(x, y, 0.38f, 0.47f, 0.035f, 0.06f) ||
                        InEllipse(x, y, 0.62f, 0.47f, 0.035f, 0.06f)) c = black;
                }
                t.SetPixel(px, py, c);
            }
        return Finish(t);
    }

    public static Sprite Heart(int size, Color color)
    {
        var t = NewTex(size);
        for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                // 归一到 [-1,1]，心形隐式方程
                float x = (px + 0.5f) / size * 2f - 1f;
                float y = (py + 0.5f) / size * 2f - 1f;
                y = -y;                 // 使心尖朝下
                x *= 1.25f; y = y * 1.25f + 0.35f;
                float v = (x * x + y * y - 1f);
                bool inside = v * v * v - x * x * y * y * y < 0f;
                t.SetPixel(px, py, inside ? color : new Color(0, 0, 0, 0));
            }
        return Finish(t);
    }

    /// <summary>信封图标：白纸 + 深色 V 折线</summary>
    public static Sprite Envelope(int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        Color paper = new Color(0.98f, 0.98f, 1f, 1f);
        Color line = new Color(0.3f, 0.4f, 0.7f, 1f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = paper;
                // 边框
                if (x < 2 || x >= w - 2 || y < 2 || y >= h - 2) c = line;
                // V 折线：从上两角连到中下
                float fx = x / (float)(w - 1), fy = y / (float)(h - 1);
                float vy = 1f - Mathf.Abs(fx - 0.5f) * 2f;   // 顶部两角=0，中间=1
                if (Mathf.Abs(fy - vy) < 0.06f && fy > 0.35f) c = line;
                t.SetPixel(x, y, c);
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100);
    }

    // ---------- helpers ----------

    static Texture2D NewTex(int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Bilinear;
        return t;
    }

    static Sprite Finish(Texture2D t)
    {
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100);
    }

    static bool InEllipse(float x, float y, float cx, float cy, float rx, float ry)
    {
        float dx = (x - cx) / rx, dy = (y - cy) / ry;
        return dx * dx + dy * dy <= 1f;
    }

    static bool InTri(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
    {
        float d1 = Sign(x, y, ax, ay, bx, by);
        float d2 = Sign(x, y, bx, by, cx, cy);
        float d3 = Sign(x, y, cx, cy, ax, ay);
        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(hasNeg && hasPos);
    }

    static float Sign(float px, float py, float ax, float ay, float bx, float by)
    {
        return (px - bx) * (ay - by) - (ax - bx) * (py - by);
    }
}
