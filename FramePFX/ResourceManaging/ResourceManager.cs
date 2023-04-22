using System;
using System.Collections.Generic;

namespace FramePFX.Core.Resources {
    public class ResourceManager {
        private readonly Dictionary<string, ResourceItem> uuidToItem;

        public ResourceManager() {
            this.uuidToItem = new Dictionary<string, ResourceItem>();
        }

        public ResourceItem AddResource(string id, ResourceItem item) {
            if (item == null)
                throw new ArgumentNullException(nameof(item), "ResourceItem cannot be null");
            ValidateId(id);
            this.uuidToItem.TryGetValue(id, out ResourceItem oldItem);
            this.uuidToItem[id] = item;
            return oldItem;
        }

        public ResourceItem RemoveItem(string id) {
            ValidateId(id);
            this.uuidToItem.TryGetValue(id, out ResourceItem item);
            return this.uuidToItem.Remove(id) ? item : null;
        }

        public bool RenameResource(ResourceItem item, string newId) {
            if (item == null)
                throw new ArgumentNullException(nameof(item), "ResourceItem cannot be null");
            if (string.IsNullOrWhiteSpace(item.Id))
                throw new ArgumentException("Old Item ID cannot be null, empty or consist of entirely whitespaces", nameof(item));
            if (string.IsNullOrWhiteSpace(newId))
                throw new ArgumentException("New ID cannot be null, empty or consist of entirely whitespaces", nameof(newId));
            if (this.uuidToItem.TryGetValue(item.Id, out ResourceItem idItem)) {
                if (ReferenceEquals(item, idItem)) {
                    item.Id = newId;
                    return true;
                }
                else {
                    throw new Exception($"Existing item and parameter item have the same ID but are not reference equal. Id = {item.Id}");
                }
            }
            else {
                return false;
            }
        }

        public bool RenameResource(string oldId, string newId) {
            if (string.IsNullOrWhiteSpace(oldId))
                throw new ArgumentException("Old ID cannot be null, empty or consist of entirely whitespaces", nameof(oldId));
            if (string.IsNullOrWhiteSpace(newId))
                throw new ArgumentException("New ID cannot be null, empty or consist of entirely whitespaces", nameof(newId));
            if (this.uuidToItem.TryGetValue(oldId, out ResourceItem item)) {
                item.Id = newId;
                return true;
            }
            else {
                return false;
            }
        }

        public static void ValidateId(string id, string paramName = "id") {
            if (string.IsNullOrWhiteSpace(id)) {
                throw new ArgumentException("ID cannot be null, empty or consist of entirely whitespaces", paramName ?? "id");
            }
        }

        public bool IsRegistered(ResourceItem item) {
            if (item == null)
                throw new ArgumentNullException(nameof(item), "ResourceItem cannot be null");
            return !string.IsNullOrWhiteSpace(item.Id) && this.uuidToItem.ContainsKey(item.Id);
        }
    }
}