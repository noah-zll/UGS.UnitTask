using System;
using System.Collections.Generic;

namespace UGS.UnitTask
{
    public sealed class UnitTaskDebugTraceBuffer : IUnitTaskDebugTraceSink
    {
        private readonly UnitTaskDecisionRecord[] _buffer;
        private int _nextIndex;
        private int _count;

        public UnitTaskDebugTraceBuffer(int capacity)
        {
            if (capacity <= 0) capacity = 0;

            _buffer = capacity == 0 ? Array.Empty<UnitTaskDecisionRecord>() : new UnitTaskDecisionRecord[capacity];
            _nextIndex = 0;
            _count = 0;
        }

        public int Capacity => _buffer.Length;
        public int Count => _count;

        public void Record(in UnitTaskDecisionRecord record)
        {
            if (_buffer.Length == 0)
            {
                return;
            }

            _buffer[_nextIndex] = record;
            _nextIndex++;
            if (_nextIndex >= _buffer.Length)
            {
                _nextIndex = 0;
            }

            if (_count < _buffer.Length)
            {
                _count++;
            }
        }

        public UnitTaskDecisionRecord[] ToArray()
        {
            if (_count == 0)
            {
                return Array.Empty<UnitTaskDecisionRecord>();
            }

            var result = new UnitTaskDecisionRecord[_count];
            CopyTo(result);
            return result;
        }

        public void CopyTo(UnitTaskDecisionRecord[] destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (destination.Length < _count) throw new ArgumentException("Destination array is too small.", nameof(destination));

            if (_count == 0)
            {
                return;
            }

            var startIndex = _nextIndex - _count;
            if (startIndex < 0)
            {
                startIndex += _buffer.Length;
            }

            var firstPart = Math.Min(_count, _buffer.Length - startIndex);
            Array.Copy(_buffer, startIndex, destination, 0, firstPart);
            var remaining = _count - firstPart;
            if (remaining > 0)
            {
                Array.Copy(_buffer, 0, destination, firstPart, remaining);
            }
        }
    }
}

