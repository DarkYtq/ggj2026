using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 拖拽小猫来移动【整个小组件】：实际移动的是 moveTarget（默认为猫的父级容器 Widget），
/// 所以计时器、信箱、小猫会作为一个整体一起挪动，而不是猫单独跑掉。
/// 与 ClickHandler 并存：拖拽超过 EventSystem 阈值才算拖动，因此不会误触发单击/双击
/// （EventSystem 在发生拖拽后不会再发 OnPointerClick）。
/// 挂在 Cat 物体上。
/// </summary>
public class CatDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Tooltip("拖拽时实际移动的容器（整组一起动）。留空则自动取本物体的父级。")]
    public RectTransform moveTarget;

    private Canvas _canvas;

    void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        if (moveTarget == null) moveTarget = transform.parent as RectTransform;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        DesktopPet.DragActive = true;   // 拖拽期间禁止切换穿透
    }

    public void OnDrag(PointerEventData e)
    {
        if (moveTarget == null) return;
        float scale = (_canvas != null) ? _canvas.scaleFactor : 1f;
        if (scale <= 0f) scale = 1f;
        moveTarget.anchoredPosition += e.delta / scale;   // 整个容器跟着走
    }

    public void OnEndDrag(PointerEventData e)
    {
        DesktopPet.DragActive = false;
    }
}
