using System;
using UniInject;
using UnityEngine;
using UnityEngine.EventSystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class NoteAreaScrollingDragListener : INeedInjection, IInjectionFinishedListener, IDragListener<NoteAreaDragEvent>
{
    private const float TouchGestureMoveInSameDirectionThreshold = 100f;
    
    [Inject]
    private NoteAreaControl noteAreaControl;

    [Inject]
    private NoteAreaDragControl noteAreaDragControl;

    private int viewportXBeginDrag;
    private int viewportYBeginDrag;

    bool isCanceled;

    private float twoFingerGestureStartDistance;

    public void OnInjectionFinished()
    {
        noteAreaDragControl.AddListener(this);
    }

    public void OnBeginDrag(NoteAreaDragEvent dragEvent)
    {
        isCanceled = false;
        if (dragEvent.GeneralDragEvent.InputButton != (int)PointerEventData.InputButton.Middle
            && Touch.activeTouches.Count == 0)
        {
            CancelDrag();
            return;
        }

        twoFingerGestureStartDistance = -1;
        viewportXBeginDrag = noteAreaControl.ViewportX;
        viewportYBeginDrag = noteAreaControl.ViewportY;
    }

    public void OnDrag(NoteAreaDragEvent dragEvent)
    {
        if (dragEvent.GeneralDragEvent.InputButton != (int)PointerEventData.InputButton.Middle
            && Touch.activeTouches.Count < 2)
        {
            return;
        }

        if (Touch.activeTouches.Count == 2)
        {
            if (twoFingerGestureStartDistance < 0)
            {
                twoFingerGestureStartDistance = Vector2.Distance(Touch.activeTouches[0].screenPosition, Touch.activeTouches[1].screenPosition);
            }
            else
            {
                // The touches must all move in the same direction. Thus, their distance to each other must be (near) constant.
                float distance = Vector2.Distance(Touch.activeTouches[0].screenPosition, Touch.activeTouches[1].screenPosition);
                if (Math.Abs(distance - twoFingerGestureStartDistance) > TouchGestureMoveInSameDirectionThreshold)
                {
                    CancelDrag();
                    return;
                }
            }
        }
        
        int newViewportX = viewportXBeginDrag - dragEvent.MillisDistance;
        int newViewportY = viewportYBeginDrag + dragEvent.MidiNoteDistance;
        noteAreaControl.SetViewport(newViewportX, newViewportY, noteAreaControl.ViewportWidth, noteAreaControl.ViewportHeight);
    }

    public void OnEndDrag(NoteAreaDragEvent dragEvent)
    {
        // Do nothing, scrolling was done already in OnDrag.
    }

    public void CancelDrag()
    {
        isCanceled = true;
    }

    public bool IsCanceled()
    {
        return isCanceled;
    }
}
