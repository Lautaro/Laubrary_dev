using UnityEngine;
using Laubrary.Larder;

/// Demo-only: at play time, fills rows of shelves with freshly-rolled Wares. Each is a GameObject carrying a
/// SpriteRenderer + a BoxCollider2D (so it can be clicked) + a ShelfWare (which generates its own damage stages and
/// destruction). Every Ware gets its own throwaway WareSpec rolled off one master seed, so the shelf is reproducible
/// but varied. Not shipped with the package.
public class LarderDemoShelf : MonoBehaviour
{
    public int rows = 3;
    public int cols = 6;
    public float hSpacing = 1.15f;
    public float vSpacing = 1.35f;
    public int seed = 1;

    void Start() => Build();

    void Build()
    {
        var rng = new System.Random(seed);
        float x0 = -(cols - 1) * hSpacing * 0.5f;
        float y0 = -(rows - 1) * vSpacing * 0.5f;

        for (int r = 0; r < rows; r++)
        {
            // a shelf plank behind each row
            MakePlank(new Vector3(0f, y0 + r * vSpacing - 0.55f, 0.1f), cols * hSpacing + 0.4f);

            for (int c = 0; c < cols; c++)
            {
                var spec = ScriptableObject.CreateInstance<WareSpec>();
                spec.Randomize(new System.Random(rng.Next()));

                var go = new GameObject($"Ware_{r}_{c}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(x0 + c * hSpacing, y0 + r * vSpacing, 0f);

                var srr = go.AddComponent<SpriteRenderer>();
                srr.sortingOrder = 10 + r;

                go.AddComponent<BoxCollider2D>();
                var sw = go.AddComponent<ShelfWare>();
                sw.spec = spec; // ShelfWare generates + shows stage 0 in its own Start, then fits the collider
            }
        }
    }

    void MakePlank(Vector3 pos, float width)
    {
        var go = new GameObject("Shelf");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(width, 0.18f, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = UnitSprite();
        sr.color = new Color(0.28f, 0.22f, 0.16f);
        sr.sortingOrder = 0;
    }

    static Sprite plank;
    static Sprite UnitSprite()
    {
        if (plank != null) return plank;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        tex.SetPixels32(new[] { new Color32(255, 255, 255, 255) });
        tex.Apply();
        plank = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return plank;
    }
}
