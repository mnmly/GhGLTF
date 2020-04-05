using System;
using System.Threading.Tasks;

namespace MNML
{
    public class Debouncer
    {
        private readonly TimeSpan delay;
        private int debounceCount;

        public Debouncer(TimeSpan delay)
        {
            this.delay = delay;
        }

        public void Debounce(Action action)
        {
            int current = ++debounceCount;
            Task.Delay(delay).ContinueWith((task) =>
            {
                if (debounceCount == current)
                    action();
            });
        }
    }
}