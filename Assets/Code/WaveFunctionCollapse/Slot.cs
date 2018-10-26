﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Slot {
	public Vector3i Position;

	// List of modules that can still be placed here
	public HashSet<int> Modules;

	// Direction -> Module -> Number of entries in this.Modules that allow that module as a neighbor in that direction
	public int[][] NeighborCandidateHealth;

	// Which modules occupies this slot, -1 for uncollapsed
	public int ModuleIndex;

	private MapGenerator mapGenerator;

	private IMap map;

	public Module Module {
		get {
			return this.mapGenerator.Modules[this.ModuleIndex];
		}
	}

	public bool Collapsed {
		get {
			return this.ModuleIndex != -1;
		}
	}

	public int Entropy {
		get {
			return this.Modules.Count;
		}
	}

	public BlockBehaviour BlockBehaviour;

	public Slot(Vector3i position, MapGenerator mapGenerator, IMap map) {
		this.Position = position;
		this.mapGenerator = mapGenerator;
		this.map = map;
		this.Modules = new HashSet<int>(Enumerable.Range(0, mapGenerator.Modules.Length));
		this.ModuleIndex = -1;
	}

	public Slot(Vector3i position, MapGenerator mapGenerator, Slot prototype) : this(position, mapGenerator, mapGenerator) {
		this.NeighborCandidateHealth = prototype.NeighborCandidateHealth.Select(a => a.ToArray()).ToArray();
		this.Modules = new HashSet<int>(prototype.Modules);
	}

	// TODO only look up once and then cache???
	private Slot neighbor(int direction) {
		return this.map.GetSlot(this.Position + Orientations.Direction[direction]);
	}

	public void Collapse(int index) {
		if (this.Collapsed) {
			Debug.LogWarning("Trying to collapse already collapsed slot.");
			return;
		}

		this.ModuleIndex = index;

#if UNITY_EDITOR
		this.checkConsistency(index);
#endif
		var toRemove = this.Modules.ToList();
		toRemove.Remove(index);
		this.RemoveModules(toRemove);

		this.Build();
	}

	private void checkConsistency(int index) {
		for (int d = 0; d < 6; d++) {
			if (this.neighbor(d) != null && this.neighbor(d).Collapsed && !this.neighbor(d).Module.PossibleNeighbours[(d + 3) % 6].Contains(index)) {
				this.markRed();
				// This would be a result of inconsistent code, should not be possible.
				throw new Exception("Illegal collapse, not in neighbour list. (Incompatible connectors)");
			}
		}

		if (!this.Modules.Contains(index)) {
			this.markRed();
			// This would be a result of inconsistent code, should not be possible.
			throw new Exception("Illegal collapse!");
		}
	}

	public void CollapseRandom() {
		if (!this.Modules.Any()) {
			throw new Exception("No modules to select.");	
		}
		if (this.Collapsed) {
			throw new Exception("Slot is already collapsed.");
		}
		var candidates = this.Modules.ToList();
		float max = candidates.Select(i => this.mapGenerator.Modules[i].Probability).Sum();
		float roll = UnityEngine.Random.Range(0f, max);
		float p = 0;
		foreach (var candidate in candidates) {
			p += this.mapGenerator.Modules[candidate].Probability;
			if (p >= roll) {
				this.Collapse(candidate);
				return;
			}			
		}
		this.Collapse(candidates.First());
	}

	public void RemoveModules(List<int> modulesToRemove) {
		var affectedNeighbouredModules = Enumerable.Range(0, 6).Select(_ => new List<int>()).ToArray();

		foreach (int module in modulesToRemove) {
			if (!this.Modules.Contains(module) || module == this.ModuleIndex) {
				continue;
			}
			for (int d = 0; d < 6; d++) {
				foreach (int possibleNeighbour in this.mapGenerator.Modules[module].PossibleNeighbours[d]) {
					if (this.NeighborCandidateHealth[d][possibleNeighbour] == 1) {
						affectedNeighbouredModules[d].Add(possibleNeighbour);
					}
					this.NeighborCandidateHealth[d][possibleNeighbour]--;
				}
			}
			this.Modules.Remove(module);
		}

		if (this.Modules.Count == 0) {
			this.markRed();
			throw new Exception("Wavefunction collapse failed.");
		}

		for (int d = 0; d < 6; d++) {
			if (affectedNeighbouredModules[d].Any() && this.neighbor(d) != null) {
				this.neighbor(d).RemoveModules(affectedNeighbouredModules[d]);
			}
		}
	}

	private void markRed() {
		var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
		cube.transform.parent = this.mapGenerator.transform;
		cube.GetComponent<MeshRenderer>().sharedMaterial.color = Color.red;
		cube.transform.position = this.GetPosition();
	}

	public void Build() {
		if (!this.Collapsed || this.Module.Prototype.Spawn == false) {
			return;
		}

		AbstractModulePrototype model = this.Module.Prototype;
		if (this.Module.Models.Count > 1) {
			float max = this.Module.Models.Select(m => m.Probability).Sum();
			float roll = UnityEngine.Random.Range(0f, max);
			float p = 0;
			foreach (var candidate in this.Module.Models) {
				p += candidate.Probability;
				if (p >= roll) {
					model = candidate;
					break;
				}
			}
		}

		var gameObject = GameObject.Instantiate(model.gameObject);
		GameObject.DestroyImmediate(gameObject.GetComponent<AbstractModulePrototype>());
		gameObject.transform.parent = this.mapGenerator.transform;
		gameObject.transform.position = this.GetPosition();
		gameObject.transform.rotation = Quaternion.Euler(Vector3.up * 90f * this.Module.Rotation);
		var blockBehaviour = gameObject.AddComponent<BlockBehaviour>();
		blockBehaviour.Prototype = this.Module.Prototype;
		blockBehaviour.Neighbours = new BlockBehaviour[6];
		for (int i = 0; i < 6; i++) {
			if (this.neighbor(i) != null && this.neighbor(i).BlockBehaviour != null) {
				var otherBlock = this.neighbor(i).BlockBehaviour;
				blockBehaviour.Neighbours[i] = otherBlock;
				otherBlock.Neighbours[(i + 3) % 6] = blockBehaviour;
			}
		}
	}

	public Vector3 GetPosition() {
		return this.mapGenerator.GetWorldspacePosition(this.Position);
	}

	public void EnforeConnector(int direction, int connector) {
		var toRemove = this.Modules.Where(i => !this.mapGenerator.Modules[i].Fits(direction, connector)).ToList();
		this.RemoveModules(toRemove);
	}

	public void ExcludeConnector(int direction, int connector) {
		var toRemove = this.Modules.Where(i => this.mapGenerator.Modules[i].Fits(direction, connector)).ToList();
		this.RemoveModules(toRemove);
	}

	public override int GetHashCode() {
		return this.Position.GetHashCode();
	}
}
