#!/bin/sh
cd /app/SimulationCraft/profiles/MID1
for f in MID1_*.simc; do
  base=${f%.simc}
  name=$(head -1 "$f" | sed 's/.*="\([^"]*\)".*/\1/')
  if [ "$base" = "$name" ]; then
    echo "SAME|$base"
  else
    echo "DIFF|$base|$name"
  fi
done | sort
