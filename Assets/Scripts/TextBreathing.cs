using UnityEngine;
using UnityEngine.UI;

public class TextBreathing : MonoBehaviour
{
    public float speed = 2f;
    public float minAlpha = 0.3f;
    public float maxAlpha = 1f;

    private Text text;
    private Color originalColor;

    void Start()
    {
        text = GetComponent<Text>();
        if (text != null) originalColor = text.color;
    }

    void Update()
    {
        if (text != null)
        {
            float alpha = Mathf.Lerp(minAlpha, maxAlpha, (Mathf.Sin(Time.time * speed) + 1f) / 2f);
            text.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
        }
    }
}