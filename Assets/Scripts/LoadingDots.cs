using UnityEngine;

public class LoadingDots : MonoBehaviour
{
    public RectTransform[] dots;          // 5个圆点
    public float orbitRadius = 50f;       // 旋转半径
    public float orbitSpeed = 180f;       // 旋转速度（度/秒）

    private float angle = 0f;

    void Start()
    {
        if (dots == null || dots.Length == 0)
            dots = GetComponentsInChildren<RectTransform>();
    }

    void Update()
    {
        angle += orbitSpeed * Time.deltaTime;
        for (int i = 0; i < dots.Length; i++)
        {
            float rad = (angle + i * (360f / dots.Length)) * Mathf.Deg2Rad;
            dots[i].anchoredPosition = new Vector2(Mathf.Cos(rad) * orbitRadius, Mathf.Sin(rad) * orbitRadius);
        }
    }
}