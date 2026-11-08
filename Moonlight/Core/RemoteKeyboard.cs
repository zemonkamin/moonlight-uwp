using System;
using System.Collections.Generic;
using Windows.System;

namespace Moonlight.Core
{
    // Keep physical key identity, including E0, for matched key-up events and
    // focus loss. This distinguishes right modifiers and keypad Enter.
    internal sealed class RemoteKeyboard
    {
        private const int Extended = 0x10;
        private readonly Dictionary<int, int> pressed = new Dictionary<int, int>();
        private readonly Action<int, bool, int> send;
        public RemoteKeyboard(Action<int, bool, int> sender) { send = sender; }
        private static int Translate(VirtualKey key, uint scanCode, bool extended)
        {
            if (key == VirtualKey.Shift) return scanCode == 0x36 ? 0xa1 : 0xa0;
            if (key == VirtualKey.Control) return extended ? 0xa3 : 0xa2;
            if (key == VirtualKey.Menu) return extended ? 0xa5 : 0xa4;
            return (int)key;
        }
        public void Press(VirtualKey virtualKey, uint scanCode, bool extended, int modifiers)
        {
            int key = Translate(virtualKey, scanCode, extended);
            if (key <= 0 || key > 255) return;
            int flag = extended ? Extended : 0;
            pressed[key | (extended ? 0x100 : 0)] = flag;
            send(key, true, modifiers | flag);
        }
        public void Release(VirtualKey virtualKey, uint scanCode, bool extended, int modifiers)
        {
            int key = Translate(virtualKey, scanCode, extended), id = key | (extended ? 0x100 : 0);
            int flag;
            if (!pressed.TryGetValue(id, out flag)) return;
            pressed.Remove(id); send(key, false, modifiers | flag);
        }
        public void ReleaseAll()
        {
            foreach (var key in pressed) send(key.Key & 0xff, false, key.Value);
            pressed.Clear();
        }
    }
}
