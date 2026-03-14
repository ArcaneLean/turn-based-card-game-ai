# Turn-Based Card Game AI Agents

This repository contains AI implementations for a turn-based card game, showcasing multiple strategic algorithms and a modular personality system. The project is based on my Master's Thesis, focusing on designing AI opponents that are both **strategically competent** and **behaviorally diverse**.

## Features

- Implementation of three novel AI techniques:
  - Determinized Greedy Search (DGS)
  - Determinized Greedy Rollout Search (DGRS)
  - Single-Agent Budgeted DESPOT (SAB-DESPOT)
- Comparison to Information Set Monte Carlo Tree Search (ISMCTS)
- Modular personality-driven AI system implemented in `Personalities.cs` using weighted intents and finite state machines
- Utilities for AI support included in `/ai`
- DOT tree generation code for visualization in `/dot`
- Architecture diagrams and visualizations illustrating AI decision flow

## Notes

- Simulation analysis scripts originally used in Unity are not included, but the **core AI algorithms and system architecture** are fully available.
- Diagrams and visualizations from the thesis are included in the `diagrams/` folder to illustrate results.

## Repository Structure

```

turn-based-card-game-ai/
├── ai/                  # AI algorithm implementations and utility scripts
├── dot/                 # Code for DOT tree generation
├── Personalities.cs     # Personality system implementation
└── diagrams/            # Architecture and analysis visuals

```

## Technologies Used

- C# / Unity (AI integration)
- Python (optional analysis scripts — not included)
- Unity VCS for version control

## How to Use

1. Review the AI classes and utilities in `/ai`.  
2. Examine the personality system in `Personalities.cs`.  
3. Use `/dot` scripts to generate DOT trees for AI decision visualization.  
4. Integrate into your own Unity project to simulate AI opponents.  
5. See `/diagrams` for architecture diagrams and evaluation visualizations.

## Author

**Aron Davids** — software developer and game AI researcher  
Based on Master’s Thesis: *Evaluating Strategic AI Techniques and Personality-Driven Behavior for Commercial Turn-Based Card Games*
