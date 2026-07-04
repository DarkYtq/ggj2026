using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 区分单击 / 双击。双击立即触发 onDouble；单击等待 doubleClickTime 内没有第二次点击才触发 onSingle。
/// </summary>
public class ClickHandler : MonoBehaviour, IPointerClickHandler
{
    public float doubleClickTime = 0.25f;
    public System.Action onSingle;
    public System.Action onDouble;

    private float _lastClickTime = -10f;
    private bool _pendingSingle;

    public void OnPointerClick(PointerEventData e)
    {
        if (Time.unscaledTime - _lastClickTime < doubleClickTime)
        {
            _pendingSingle = false;          // 取消待触发的单击
            _lastClickTime = -10f;
            onDouble?.Invoke();
        }
        else
        {
            _lastClickTime = Time.unscaledTime;
            _pendingSingle = true;
            StartCoroutine(SingleAfterDelay());
        }
    }

    IEnumerator SingleAfterDelay()
    {
        yield return new WaitForSecondsRealtime(doubleClickTime);
        if (_pendingSingle)
        {
            _pendingSingle = false;
            onSingle?.Invoke();
        }
    }
}
