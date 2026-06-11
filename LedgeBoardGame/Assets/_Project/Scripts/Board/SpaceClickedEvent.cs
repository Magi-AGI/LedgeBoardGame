using System;
using System.Collections.Generic;

namespace Magi.LedgeBoardGame.Board
{
    public static class SpaceClickedEvent
    {
        private static readonly List<Action<SpaceView>> _listeners = new List<Action<SpaceView>>();

        public static void Register(Action<SpaceView> listener)
        {
            if (listener == null) return;
            if (!_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }
        }

        public static void Unregister(Action<SpaceView> listener)
        {
            if (listener == null) return;
            _listeners.Remove(listener);
        }

        public static void Raise(SpaceView view)
        {
            if (view == null) return;

            foreach (var listener in _listeners)
            {
                listener(view);
            }
        }
    }
}

