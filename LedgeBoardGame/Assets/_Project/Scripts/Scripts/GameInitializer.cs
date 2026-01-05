using System.Collections.Generic;
using UnityEngine;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Spec;
using Magi.LedgeBoardGame.Rules;
using UnityDebug = UnityEngine.Debug;

namespace Magi.LedgeBoardGame
{
    /// <summary>
    /// Creates GameState and GameRules from players and spec JSON.
    /// Assign players/spec in inspector; GameController can call Initialize().
    /// </summary>
    public class GameInitializer : MonoBehaviour
    {
        [SerializeField] private TextAsset ledgeSpecJson;

        public (GameState state, GameRules rules, LedgeRuntimeConfig config) Initialize(List<Player> players)
        {
            LedgeRuntimeConfig runtimeConfig = null;
            var useSpec = false;

            if (ledgeSpecJson != null && !string.IsNullOrEmpty(ledgeSpecJson.text))
            {
                var spec = LedgeGameSpecLoader.LoadFromJson(ledgeSpecJson.text);
                if (spec != null)
                {
                    LedgeSpecValidator.Validate(spec);
                    runtimeConfig = LedgeRuntimeConfig.FromSpec(spec);
                    useSpec = true;
                }
                else if (Application.isEditor)
                {
                    UnityDebug.LogError("GameInitializer: Failed to parse ledge spec JSON.");
                    return (null, null, null);
                }
            }
            else if (Application.isEditor)
            {
                UnityDebug.LogError("GameInitializer: No ledgeSpecJson assigned.");
                return (null, null, null);
            }

            var state = new GameState(players, runtimeConfig);
            var rules = new GameRules(useSpec ? runtimeConfig : null);
            return (state, rules, runtimeConfig);
        }
    }
}
