// 9/29/2025 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using UnityEngine;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Config;

namespace Magi.LedgeBoardGame.Board
{
    public class BoardPresenter : MonoBehaviour
    {
        [SerializeField] private BoardLayoutConfig layoutConfig;
        [SerializeField] private SpaceView spaceViewPrefab;

        private BoardState boardState;
        private Dictionary<int, SpaceView> spaceViews;

        public void Initialize(BoardState state) { }
        public void UpdateView() { }
        public void HighlightValidMoves(List<SpaceId> spaces) { }
    }
}