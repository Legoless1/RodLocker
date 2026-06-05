using UnityEngine;

namespace RodLocker
{
	public class Loader
	{
		/// <summary>
		/// This method is run by Winch to initialize your mod
		/// </summary>
		public static void Initialize()
		{
			var gameObject = new GameObject(nameof(RodLocker));
			gameObject.AddComponent<RodLocker>();
			GameObject.DontDestroyOnLoad(gameObject);
		}
	}
}
