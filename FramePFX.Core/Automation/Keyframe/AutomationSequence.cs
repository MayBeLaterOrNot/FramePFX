using System;
using System.Collections.Generic;
using System.Numerics;
using FramePFX.Core.Automation.Keys;
using FramePFX.Core.RBC;

namespace FramePFX.Core.Automation.Keyframe {
    /// <summary>
    /// Contains all of the key frames for a specific <see cref="AutomationKey"/>
    /// </summary>
    public class AutomationSequence : IRBESerialisable {
        private readonly List<KeyFrame> keyFrameList;

        /// <summary>
        /// Whether or not this sequence has any key frames
        /// </summary>
        public bool IsEmpty => this.keyFrameList.Count < 1;

        /// <summary>
        /// A keyframe that stores an override value, which overrides any automation
        /// </summary>
        public KeyFrame OverrideKeyFrame { get; }

        /// <summary>
        /// Whether or not the current automation sequence is in override mode or not. When in override mode,
        /// the automation engine cannot update the value of any parameter, even if it has key frames
        /// <para>
        /// Alternative name: IsDisabled
        /// </para>
        /// </summary>
        public bool IsOverrideEnabled { get; set; }

        public bool HasKeyFrames => this.keyFrameList.Count > 0;

        /// <summary>
        /// Returns true when <see cref="IsOverrideEnabled"/> is false, and there are key frames present, meaning the automation engine is operating in normal operation
        /// </summary>
        public bool IsAutomationInUse => !this.IsOverrideEnabled && this.HasKeyFrames;

        /// <summary>
        /// An enumerable of all the key frames, ordered by the timestamp (small to big)
        /// </summary>
        public IEnumerable<KeyFrame> KeyFrames => this.keyFrameList;

        public AutomationDataType DataType { get; }

        public AutomationData AutomationData { get; }

        public AutomationKey Key { get; }

        /// <summary>
        /// An event fired, notifying any listeners to query their live value from the automation data
        /// </summary>
        public UpdateAutomationValueEventHandler UpdateValue;

        public AutomationSequence(AutomationData automationData, AutomationKey key) {
            this.AutomationData = automationData;
            this.Key = key;
            this.keyFrameList = new List<KeyFrame>();
            this.DataType = key.DataType;
            this.OverrideKeyFrame = key.CreateKeyFrame();
            this.OverrideKeyFrame.OwnerSequence = this;
        }

        /// <summary>
        /// Invokes the <see cref="UpdateValue"/> event, allowing any listeners to re-query their actual value at the given frame
        /// </summary>
        /// <param name="engine">The engine that caused this update</param>
        /// <param name="frame">The frame</param>
        public void DoUpdateValue(AutomationEngine engine, long frame) {
            this.UpdateValue?.Invoke(this, frame);
        }

        #region Helper Getter Functions

        public bool TryGetDoubleValue(long time, out double value) {
            ValidateType(AutomationDataType.Double, this.DataType);
            return this.TryGetValueInternal(time, x => ((KeyFrameDouble) x).Value, (t, a, b) => ((KeyFrameDouble) a).Interpolate(t, (KeyFrameDouble) b), out value);
        }

        public double GetDoubleValue(long time) {
            ValidateType(AutomationDataType.Double, this.DataType);
            return this.TryGetDoubleValue(time, out double value) ? value : ((KeyDescriptorDouble) this.Key.Descriptor).DefaultValue;
        }

        public bool TryGetLongValue(long time, out long value) {
            ValidateType(AutomationDataType.Long, this.DataType);
            return this.TryGetValueInternal(time, x => ((KeyFrameLong) x).Value, (t, a, b) => ((KeyFrameLong) a).Interpolate(t, (KeyFrameLong) b), out value);
        }

        public long GetLongValue(long time) {
            ValidateType(AutomationDataType.Long, this.DataType);
            return this.TryGetLongValue(time, out long value) ? value : ((KeyDescriptorLong) this.Key.Descriptor).DefaultValue;
        }

        public bool TryGetBooleanValue(long time, out bool value) {
            ValidateType(AutomationDataType.Boolean, this.DataType);
            return this.TryGetValueInternal(time, x => ((KeyFrameBoolean) x).Value, (t, a, b) => ((KeyFrameBoolean) a).Interpolate(t, (KeyFrameBoolean) b), out value);
        }

        public bool GetBooleanValue(long time) {
            ValidateType(AutomationDataType.Boolean, this.DataType);
            return this.TryGetBooleanValue(time, out bool value) ? value : ((KeyDescriptorBoolean) this.Key.Descriptor).DefaultValue;
        }

        public bool TryGetVector2Value(long time, out Vector2 value) {
            ValidateType(AutomationDataType.Vector2, this.DataType);
            return this.TryGetValueInternal(time, x => ((KeyFrameVector2) x).Value, (t, a, b) => ((KeyFrameVector2) a).Interpolate(t, (KeyFrameVector2) b), out value);
        }

        public Vector2 GetVector2Value(long time) {
            ValidateType(AutomationDataType.Vector2, this.DataType);
            return this.TryGetVector2Value(time, out Vector2 value) ? value : ((KeyDescriptorVector2) this.Key.Descriptor).DefaultValue;
        }

        private bool TryGetValueInternal<T>(long time, Func<KeyFrame, T> toValue, Func<long, KeyFrame, KeyFrame, T> interpolate, out T value) {
            if (this.IsOverrideEnabled || this.IsEmpty) {
                value = toValue(this.OverrideKeyFrame);
                return true;
            }

            if (!this.GetKeyFramesForFrame(time, out KeyFrame a, out KeyFrame b, out int index)) {
                value = default;
                return false;
            }

            // pass `time` parameter to the interpolate function to remove closure allocation; performance helper
            value = b == null ? toValue(a) : interpolate(time, a, b);
            return true; // ok
        }

        #endregion

        // not used
        public int BinarySearch(long frame) {
            List<KeyFrame> list = this.keyFrameList;
            int i = 0, j = list.Count - 1;
            while (i <= j) {
                int k = i + (j - i >> 1);
                long time = list[k].Timestamp;
                if (time == frame) {
                    return k;
                }
                else if (time < frame) {
                    i = k + 1;
                }
                else {
                    j = k - 1;
                }
            }

            return ~i;
        }

        /// <summary>
        /// Gets the two key frames that the given time should attempt to interpolate between, or a single key frame if that's all that is possible or logical
        /// <para>
        /// If the time directly intersects a key frame, then the last keyframe that intersects will be set as a, and b will be null (therefore, use a's value directly)
        /// </para>
        /// <para>
        /// If the time is before the first key frame or after the last key frame, the first/last key frame is set as a, and b will be null (therefore, use a's value directly)
        /// </para>
        /// <para>
        /// If all other cases are false, and the list is not empty, a pair of key frames will be available to interpolate between (based on a's interpolation method)
        /// </para>
        /// </summary>
        /// <param name="frame">The time</param>
        /// <param name="a">The first (or only available) key frame</param>
        /// <param name="b">The second key frame, may be null under certain conditions, in which case use a's value directly</param>
        /// <returns>False if there are no key frames, otherwise true</returns>
        public bool GetKeyFramesForFrame(long frame, out KeyFrame a, out KeyFrame b, out int i) {
            List<KeyFrame> list = this.keyFrameList;
            int count = list.Count;
            if (count < 1) {
                a = b = null;
                i = -1;
                return false;
            }

            KeyFrame value = list[i = 0], prev = null;
            while (true) { // node is never null, as it is only reassigned to next (which won't be null at that point)
                long valTime = value.Timestamp;
                if (frame > valTime) {
                    if (++i < count) {
                        prev = value;
                        value = list[i];
                    }
                    else { // last key frame
                        a = value;
                        b = null;
                        return true;
                    }
                }
                else if (frame < valTime) {
                    if (prev == null) { // first key frame; time is before the first node
                        a = value;
                        b = null;
                        return true;
                    }
                    else { // found pair of key frames to interpolate between
                        a = prev;
                        b = value;
                        return true;
                    }
                }
                else {
                    // get the last node next node whose timestamp equals the input time, otherwise
                    // use the last node (the input time is the same as the very last node's timestamp)
                    KeyFrame temp = value, tempPrev = prev;
                    while (temp != null && temp.Timestamp == frame) {
                        tempPrev = temp;
                        temp = ++i < count ? list[i] : null;
                    }

                    if (temp != null && tempPrev != null) {
                        a = tempPrev;
                        b = temp;
                    }
                    else {
                        a = temp ?? tempPrev;
                        b = null;
                    }

                    return true;
                }
            }
        }

        /*
            implements caching but it breaks sometimes
            public bool GetKeyFramesForFrame(long frame, out KeyFrame a, out KeyFrame b, out int i) {
                List<KeyFrame> list = this.keyFrameList;
                int count = list.Count, j;
                if (count < 1) {
                    a = b = null;
                    i = -1;
                    return false;
                }

                if (this.cache_valid) {
                    if (frame >= this.cache_time) {
                        i = this.cache_index;
                        j = count - 1;
                    }
                    else {
                        i = 0;
                        j = this.cache_index;
                    }
                }
                else {
                    i = 0;
                    j = count - 1;
                }

                KeyFrame value = list[i], prev = null;
                while (true) { // node is never null, as it is only reassigned to next (which won't be null at that point)
                    long valTime = value.Timestamp;
                    if (frame > valTime) {
                        if (++i <= j) {
                            prev = value;
                            value = list[i];
                        }
                        else { // last key frame
                            a = value;
                            b = null;
                            this.cache_valid = true;
                            this.cache_index = i - 1;
                            this.cache_time = frame;
                            return true;
                        }
                    }
                    else if (frame < valTime) {
                        this.cache_valid = true;
                        this.cache_index = i;
                        this.cache_time = frame;
                        if (prev == null) { // first key frame; time is before the first node
                            if (this.cache_valid && this.cache_index > 0) {
                                a = list[this.cache_index - 1];
                                b = value;
                                return true;
                            }

                            a = value;
                            b = null;
                            return true;
                        }
                        else { // found pair of key frames to interpolate between
                            a = prev;
                            b = value;
                            return true;
                        }
                    }
                    else {
                        // get the last node next node whose timestamp equals the input time, otherwise
                        // use the last node (the input time is the same as the very last node's timestamp)
                        KeyFrame temp = value, tempPrev = prev;
                        while (temp != null && temp.Timestamp == frame) {
                            tempPrev = temp;
                            temp = ++i <= j ? list[i] : null;
                        }

                        if (temp != null && tempPrev != null) {
                            a = tempPrev;
                            b = temp;
                        }
                        else {
                            a = temp ?? tempPrev;
                            b = null;
                        }

                        this.cache_valid = true;
                        this.cache_index = i - 1;
                        this.cache_time = frame;
                        return true;
                    }
                }
            }
         */

        /// <summary>
        /// Enumerates all of they keys are are located at the given frame
        /// </summary>
        /// <param name="frame">Target frame</param>
        /// <returns>The key frames at the given frame</returns>
        public IEnumerable<KeyFrame> GetKeyFramesAt(long frame) {
            foreach (KeyFrame keyFrame in this.keyFrameList) {
                long time = keyFrame.Timestamp;
                if (time == frame) {
                    yield return keyFrame;
                }
                else if (time > frame) {
                    yield break;
                }
            }
        }

        /// <summary>
        /// Gets the last <see cref="KeyFrame"/> at the given frame. Key frames are ordered left to right, but in the vertical axis, it is unordered
        /// </summary>
        /// <param name="frame">Target frame</param>
        /// <returns>The last key frame at the given frame, or null, if there are no key frames at the given frame</returns>
        public KeyFrame GetLastFrameExactlyAt(long frame) {
            KeyFrame last = null;
            foreach (KeyFrame keyFrame in this.keyFrameList) {
                long time = keyFrame.Timestamp;
                if (time == frame) {
                    last = keyFrame;
                }
                else if (time > frame) {
                    break;
                }
            }

            return last;
        }

        /// <summary>
        /// Inserts the given key frame based on its timestamp, and returns the index that it was inserted at
        /// </summary>
        /// <param name="newKeyFrame">The key frame to add</param>
        /// <returns>The index of the key frame</returns>
        /// <exception cref="ArgumentException">Timestamp is negative or the data type is invalid</exception>
        public int AddKeyFrame(KeyFrame newKeyFrame) {
            long timeStamp = newKeyFrame.Timestamp;
            if (timeStamp < 0)
                throw new ArgumentException("Keyframe time stamp must be non-negative: " + timeStamp, nameof(newKeyFrame));
            if (newKeyFrame.DataType != this.DataType)
                throw new ArgumentException($"Invalid key frame data type. Expected {this.DataType}, got {newKeyFrame.DataType}", nameof(newKeyFrame));
            newKeyFrame.OwnerSequence = this;
            List<KeyFrame> list = this.keyFrameList;
            for (int i = list.Count - 1; i >= 0; i--) {
                if (timeStamp >= list[i].Timestamp) {
                    list.Insert(i + 1, newKeyFrame);
                    return i + 1;
                }
            }

            list.Insert(0, newKeyFrame);
            return 0;
        }

        /// <summary>
        /// Unsafely inserts the key frame at the given index, ignoring order. Do not use!
        /// </summary>
        public void InsertKeyFrame(int index, KeyFrame newKeyFrame) {
            long timeStamp = newKeyFrame.Timestamp;
            if (timeStamp < 0)
                throw new ArgumentException("Keyframe time stamp must be non-negative: " + timeStamp, nameof(newKeyFrame));
            if (newKeyFrame.DataType != this.DataType)
                throw new ArgumentException($"Invalid key frame data type. Expected {this.DataType}, got {newKeyFrame.DataType}", nameof(newKeyFrame));
            newKeyFrame.OwnerSequence = this;
            this.keyFrameList.Insert(index, newKeyFrame);
        }

        /// <summary>
        /// Unsafely removes the key frame at the given index
        /// </summary>
        public void RemoveKeyFrame(int index) {
            this.keyFrameList[index].OwnerSequence = null;
            this.keyFrameList.RemoveAt(index);
        }

        /// <summary>
        /// Gets the key frame at the given index
        /// </summary>
        public KeyFrame GetKeyFrameAtIndex(int index) {
            return this.keyFrameList[index];
        }

        public void WriteToRBE(RBEDictionary data) {
            data.SetByte(nameof(this.DataType), (byte) this.DataType);
            data.SetBool(nameof(this.IsOverrideEnabled), this.IsOverrideEnabled);
            this.OverrideKeyFrame.WriteToRBE(data.CreateDictionary(nameof(this.OverrideKeyFrame)));

            RBEList list = data.CreateList(nameof(this.KeyFrames));
            foreach (KeyFrame keyFrame in this.keyFrameList) {
                keyFrame.WriteToRBE(list.AddDictionary());
            }
        }

        public void ReadFromRBE(RBEDictionary data) {
            AutomationDataType type = (AutomationDataType) data.GetByte(nameof(this.DataType));
            if (type != this.DataType) {
                throw new Exception($"Data and current instance data type mis-match: {type} != {this.DataType}");
            }

            this.IsOverrideEnabled = data.GetBool(nameof(this.IsOverrideEnabled), false);
            this.OverrideKeyFrame.ReadFromRBE(data.GetDictionary(nameof(this.OverrideKeyFrame)));

            List<KeyFrame> frames = new List<KeyFrame>();
            RBEList list = data.GetList(nameof(this.KeyFrames));
            foreach (RBEDictionary rbe in list.GetDictionaries()) {
                KeyFrame keyFrame = this.Key.CreateKeyFrame();
                keyFrame.ReadFromRBE(rbe);
                frames.Add(keyFrame);
            }

            frames.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            foreach (KeyFrame frame in frames) {
                frame.OwnerSequence = this;
                this.keyFrameList.Add(frame);
            }
        }

        public AutomationSequence Clone(AutomationData automationData) {
            RBEDictionary dictionary = new RBEDictionary();
            this.WriteToRBE(dictionary);
            AutomationSequence seq = new AutomationSequence(automationData, this.Key);
            seq.ReadFromRBE(dictionary);
            return seq;
        }

        public static void ValidateType(AutomationDataType expected, AutomationDataType actual) {
            if (expected != actual) {
                throw new ArgumentException($"Invalid data time. Expected {expected}, got {actual}");
            }
        }
    }
}