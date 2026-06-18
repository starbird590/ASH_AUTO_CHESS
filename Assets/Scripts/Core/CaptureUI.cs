using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 把占点区进度同步到 UI Slider。
/// </summary>
public class CaptureUI : MonoBehaviour
{
    [SerializeField] private CaptureZone captureZone;
    [SerializeField] private Slider progressSlider;

    private void Reset()
    {
        progressSlider = GetComponent<Slider>();
    }

    private void Update()
    {
        if (captureZone == null || progressSlider == null)
        {
            return;
        }

        progressSlider.minValue = 0f;
        progressSlider.maxValue = captureZone.MaxProgress;
        progressSlider.value = captureZone.CurrentProgress;
    }
}
