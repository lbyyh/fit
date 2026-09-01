using Fit.Networking.Session;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fit.UI
{
    /// <summary>最近房间列表的单行视图。</summary>
    public sealed class ServerListEntryView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _detailText;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _deleteButton;

        private LobbyMenu _menu;
        private ServerListStore.ServerEntry _entry;

        private void Awake()
        {
            _joinButton.onClick.AddListener(HandleJoin);
            _deleteButton.onClick.AddListener(HandleDelete);
        }

        public void Bind(ServerListStore.ServerEntry entry, LobbyMenu menu)
        {
            _entry = entry;
            _menu = menu;

            _nameText.text = entry.DisplayName;

            var last = entry.LastJoinedUtc.ToLocalTime();
            _detailText.text = $"{entry.Address}:{entry.Port} · {last:MM-dd HH:mm}";
        }

        private void HandleJoin()
        {
            _menu?.JoinSaved(_entry.Address, _entry.Port, _entry.DisplayName);
        }

        private void HandleDelete()
        {
            // 从 UI 移除，同时通知 store 写回磁盘
            var store = new ServerListStore();
            store.Remove(_entry.Address, _entry.Port);
            Destroy(gameObject);
        }
    }
}
