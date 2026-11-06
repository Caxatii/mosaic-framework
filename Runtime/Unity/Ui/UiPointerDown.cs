using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mosaic.Ui
{
    internal sealed class UiPointerDown : MonoBehaviour, IPointerDownHandler
    {
        public event Action Pressed;

        public void OnPointerDown(PointerEventData eventData) => Pressed?.Invoke();
    }
}
