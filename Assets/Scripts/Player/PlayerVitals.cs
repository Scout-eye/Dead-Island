using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Survie : vie / faim / soif. LOGIQUE PURE — aucune dépendance UI, caméra ou réseau.
    /// Communique uniquement par événements (instance) + un registre statique des joueurs.
    ///
    /// Simulée par le owner (la faim/soif baissent, la vie chute si les deux sont à zéro).
    /// Les joueurs distants ne simulent pas : leur mort est poussée par le réseau (SetDeadFromNetwork).
    ///
    /// Faible adhérence : un futur "chef d'orchestre" peut tout lire/piloter via ces événements
    /// et <see cref="All"/> sans coupler les composants entre eux.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVitals : MonoBehaviour
    {
        [Header("Maximums")]
        [SerializeField] private float _maxHealth = 100f;
        [SerializeField] private float _maxHunger = 100f;
        [SerializeField] private float _maxThirst = 100f;

        [Header("Drain (par seconde)")]
        [SerializeField] private float _hungerDrain = 2.0f;
        [SerializeField] private float _thirstDrain = 3.0f;
        [Tooltip("Dégâts/seconde quand faim ET soif sont à zéro.")]
        [SerializeField] private float _starveDamage = 3f;

        [Header("Valeurs courantes (visibles en jeu, mises à jour live)")]
        [SerializeField] private float _health;
        [SerializeField] private float _hunger;
        [SerializeField] private float _thirst;
        [SerializeField] private bool _isDead;
        [Tooltip("Coché = ce joueur simule ses vitals (vrai en solo / pour le joueur local).")]
        [SerializeField] private bool _simulates;

        public float Health => _health;
        public float Hunger => _hunger;
        public float Thirst => _thirst;
        public float MaxHealth => _maxHealth;
        public float MaxHunger => _maxHunger;
        public float MaxThirst => _maxThirst;
        public bool IsDead => _isDead;
        // Owner = pas de controller (prefab de test posé seul) OU controller marqué owner (local).
        public bool IsOwner => _body == null || _body.IsOwner;

        // Événements d'instance (UI, etc.). (valeur courante, valeur max)
        public event Action<float, float> HealthChanged;
        public event Action<float, float> HungerChanged;
        public event Action<float, float> ThirstChanged;
        public event Action Died;

        /// <summary>Tous les joueurs présents (local + distants). Pour spectate + détection "tous morts".</summary>
        public static readonly List<PlayerVitals> All = new List<PlayerVitals>();
        /// <summary>Émis quand TOUS les joueurs enregistrés sont morts.</summary>
        public static event Action AllDead;
        /// <summary>Émis quand les vitals du joueur LOCAL sont prêtes (pour brancher le HUD).</summary>
        public static event Action<PlayerVitals> OwnerReady;
        public static event Action OwnerGone;

        private FirstPersonController _body;

        private void Awake()
        {
            _body = GetComponent<FirstPersonController>();
            _health = _maxHealth;
            _hunger = _maxHunger;
            _thirst = _maxThirst;
        }

        private void OnEnable() => All.Add(this);

        private void OnDisable()
        {
            All.Remove(this);
            if (IsOwner) OwnerGone?.Invoke();
        }

        private void Start()
        {
            _simulates = IsOwner;
            HealthChanged?.Invoke(_health, _maxHealth);
            HungerChanged?.Invoke(_hunger, _maxHunger);
            ThirstChanged?.Invoke(_thirst, _maxThirst);
            if (IsOwner) OwnerReady?.Invoke(this);
            Debug.Log($"[Vitals] {name} : IsOwner={IsOwner} → simulation {(IsOwner ? "ACTIVE" : "désactivée (joueur distant)")}.", this);
        }

        private void Update()
        {
            _simulates = IsOwner; // reflété dans l'inspecteur pour diagnostic
            if (_isDead || !IsOwner) return;

            float dt = Time.deltaTime;
            SetHunger(_hunger - _hungerDrain * dt);
            SetThirst(_thirst - _thirstDrain * dt);
            if (_hunger <= 0f && _thirst <= 0f)
                Damage(_starveDamage * dt);
        }

        // --- API ---
        public void Damage(float amount)
        {
            if (IsDead || amount <= 0f) return;
            SetHealth(Health - amount);
            if (Health <= 0f) Kill();
        }

        public void Heal(float amount) { if (!IsDead) SetHealth(Health + amount); }
        public void Eat(float amount) => SetHunger(Hunger + amount);
        public void Drink(float amount) => SetThirst(Thirst + amount);

        public void Kill()
        {
            if (IsDead) return;
            _isDead = true;
            SetHealth(0f);
            Died?.Invoke();
            if (All.TrueForAll(v => v.IsDead)) AllDead?.Invoke();
        }

        /// <summary>Poussé par le réseau pour un joueur distant.</summary>
        public void SetDeadFromNetwork(bool dead)
        {
            if (dead && !IsDead) Kill();
        }

        // --- Internes ---
        private void SetHealth(float v) { _health = Mathf.Clamp(v, 0f, _maxHealth); HealthChanged?.Invoke(_health, _maxHealth); }
        private void SetHunger(float v) { _hunger = Mathf.Clamp(v, 0f, _maxHunger); HungerChanged?.Invoke(_hunger, _maxHunger); }
        private void SetThirst(float v) { _thirst = Mathf.Clamp(v, 0f, _maxThirst); ThirstChanged?.Invoke(_thirst, _maxThirst); }
    }
}
