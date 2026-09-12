#include "kernel-repair-policy.hpp"
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
int main() {
  int checks = 0;
  auto check = [&](bool ok) {
    ++checks;
    if (!ok)
      throw std::runtime_error("path ledger failed");
  };
  try {
    const double budget = .01;
    check(.007 < RemainingPathBudget(budget, 0));
    check(ComposePathUpper(.007, .002) < budget);
    check(ComposePathUpper(.007, .004) > budget);
    check(ComposePathUpper(.004, .004) < budget);
    check(ComposePathUpper(.006, .006) > budget);
    check(ComposePathUpper(ComposePathUpper(.004, .003), .004) > budget);
    check(RemainingPathBudget(budget, .002) <= .008);
    for (double term : {-1., std::numeric_limits<double>::infinity(),
                        std::numeric_limits<double>::quiet_NaN()}) {
      bool rejected = false;
      try {
        ComposePathUpper(.001, term);
      } catch (const Standard_Failure &) {
        rejected = true;
      }
      check(rejected);
    }
    std::cout << "PASS: " << checks << " physical path accounting checks\n";
    return 0;
  } catch (const std::exception &e) {
    std::cerr << e.what() << '\n';
  }
  return 1;
}
