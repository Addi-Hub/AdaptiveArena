using UnityEngine;

public sealed class ArenaDressing : MonoBehaviour
{
    [SerializeField] private SpriteRenderer floor;

    private void Awake()
    {
        float halfWidth = floor.bounds.extents.x;
        float halfHeight = floor.bounds.extents.y;
        Color border = new Color(0.39f, 0.29f, 0.20f);
        Color grid = new Color(0.14f, 0.19f, 0.25f);

        for (int x = -5; x <= 5; x++)
            Mark("Grid column", new Vector2(x, 0), new Vector2(0.024f, 2f * halfHeight - 0.7f), grid);
        for (int y = -3; y <= 3; y++)
            Mark("Grid row", new Vector2(0, y), new Vector2(2f * halfWidth - 0.7f, 0.024f), grid);

        for (int side = -1; side <= 1; side += 2)
        {
            Mark("Ring edge", new Vector2(side * (halfWidth - 0.35f), 0),
                new Vector2(0.055f, 2f * halfHeight - 0.7f), border);
            Mark("Ring edge", new Vector2(0, side * (halfHeight - 0.35f)),
                new Vector2(2f * halfWidth - 0.7f, 0.055f), border);
        }

        Mark("Center crest", Vector2.zero, Vector2.one * 1.65f, border, 45f);
        Mark("Center crest inset", Vector2.zero, Vector2.one * 1.53f, floor.color, 45f, 1);
        Mark("Center crest core", Vector2.zero, Vector2.one * 0.53f, border, 45f, 2);
        Mark("Player corner", new Vector2(-3.9f, 0), new Vector2(0.1f, 1.55f),
            new Color(0.24f, 0.56f, 0.53f));
        Mark("Opponent corner", new Vector2(3.9f, 0), new Vector2(0.1f, 1.55f),
            new Color(0.64f, 0.32f, 0.30f));
    }

    private void Mark(string markName, Vector2 position, Vector2 size, Color color,
        float angle = 0f, int layerOffset = 0)
    {
        var mark = new GameObject(markName).AddComponent<SpriteRenderer>();
        mark.transform.SetParent(transform, false);
        mark.transform.localPosition = position;
        mark.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        mark.transform.localScale = new Vector3(size.x, size.y, 1f);
        mark.sprite = floor.sprite;
        mark.sharedMaterial = floor.sharedMaterial;
        mark.color = color;
        mark.sortingLayerID = floor.sortingLayerID;
        mark.sortingOrder = floor.sortingOrder + 1 + layerOffset;
    }
}
