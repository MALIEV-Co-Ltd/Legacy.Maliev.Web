#pragma once
#include "kernel-repair-bounds.hpp"
namespace MalievRepair {
inline double RemainingPathBudget(double budget, double sliver) {
  if (!std::isfinite(budget) || budget <= 0 || !std::isfinite(sliver) ||
      sliver < 0)
    throw Standard_Failure("invalid physical path budget");
  const double remaining = Down(budget - sliver);
  if (remaining <= 0)
    throw Standard_Failure("range-sliver-path-budget-unavailable");
  return remaining;
}
inline double ComposePathUpper(double first, double second) {
  if (!std::isfinite(first) || first < 0 || !std::isfinite(second) ||
      second < 0)
    throw Standard_Failure("unavailable path term");
  return Up(first + second);
}
} // namespace MalievRepair
