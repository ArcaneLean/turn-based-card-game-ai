# Turn-Based Card Game AI Agents

This repository contains AI implementations for a turn-based card game, showcasing multiple strategic algorithms and a modular personality system. The project is based on my Master's Thesis, focusing on designing AI opponents that are both **strategically competent** and **behaviorally diverse**.

## Features

- Implementation of multiple AI techniques:
  - Determinized Greedy Search (DGS)
  - Determinized Greedy Rollout Search (DGRS)
  - Information Set Monte Carlo Tree Search (ISMCTS)
  - Single-Agent Budgeted DESPOT (SAB-DESPOT)
- Modular personality-driven AI system with weighted intents and finite state machines
- Simulation framework for testing AI performance and behavior diversity
- Architecture diagrams and visualizations illustrating AI decision flow

## Notes

- Simulation analysis scripts originally used in Unity are not included, but the **core AI algorithms and system architecture** are fully available.
- Diagrams and visualizations from the thesis are included in the `diagrams/` folder to illustrate results.

## Repository Structure

```

turn-based-card-game-ai/
├── ai/                 # AI algorithm implementations
├── personality/        # Personality system code
├── simulation/         # Simulation framework (Unity/C#)
├── diagrams/           # Architecture and analysis visuals
├── docs/               # Thesis summary and supporting documentation
└── README.md           # Project overview

```

## Technologies Used

- C# / Unity (AI integration)
- Python (optional analysis scripts — not included)
- Git & GitHub for version control

## How to Use

1. Review the AI classes in `ai/` and `personality/`.  
2. Integrate into your own Unity project to simulate AI opponents.  
3. See `diagrams/` for visualizations of the architecture and evaluation results.

## Author

**Arcane Lean** — software developer and game AI researcher  
Based on Master’s Thesis: *Evaluating Strategic AI Techniques and Personality-Driven Behavior for Commercial Turn-Based Card Games*
