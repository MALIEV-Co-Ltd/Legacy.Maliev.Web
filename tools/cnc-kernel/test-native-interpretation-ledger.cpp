#include "kernel-native-interpretation-ledger.hpp"
#include <iostream>
#include <limits>
#include <stdexcept>
using namespace MalievNativeInterpretation;
int main() {
  int checks = 0;
  auto check = [&](bool ok, const char *name) {
    ++checks;
    if (!ok)
      throw std::runtime_error(name);
  };
  try {
    RequiredLedger ledger;
    const size_t first = ledger.Require("coedge", "boundary0/face1/use0");
    ledger.Require("coedge", "boundary0/face1/use1");
    auto counts = ledger.Counts();
    check(counts.required == 2 && counts.unassessed == 2 &&
              counts.evaluated == 0,
          "required rows survive before any assessment");
    ledger.Finish(first, true, "bounded-within-budget", {0.002, 0.003}, .01);
    counts = ledger.Counts();
    check(counts.required == counts.evaluated + counts.unassessed &&
              counts.bounded == 1,
          "partial evaluation preserves denominator partition");
    check(!ledger.Complete(),
          "unassessed required row cannot certify completion");
    RequiredLedger excess;
    excess.Finish(
        excess.Require("coedge", "a"), true, "bounded-within-budget",
        {std::nextafter(.01, std::numeric_limits<double>::infinity())}, .01);
    check(excess.Counts().unavailable == 1 && !excess.Complete(),
          "claimed bounded one-ULP excess remains unavailable");
    RequiredLedger invalid;
    invalid.Finish(invalid.Require("coedge", "b"), true,
                   "bounded-within-budget",
                   {std::numeric_limits<double>::quiet_NaN()}, .01);
    check(invalid.Counts().unavailable == 1,
          "NaN term cannot disappear from bound sum");
    Obligation skipped;
    skipped.reason = "deadline expired before required evaluator";
    const auto skippedBefore = skipped;
    check(!AppendBoundedPathTerm(skipped, .001, .01, "placement unavailable") &&
              skipped.state == skippedBefore.state &&
              skipped.reason == skippedBefore.reason &&
              skipped.pathTerms == skippedBefore.pathTerms,
          "composition preserves an unassessed row and its reason");
    Obligation exceeded;
    exceeded.state = ObligationState::Exceeded;
    exceeded.reason = "source residual exceeds policy";
    exceeded.upper = .02;
    exceeded.pathTerms = {.02};
    exceeded.pathTermKinds = {"post-trim-lift-residual"};
    check(!AppendBoundedPathTerm(exceeded, .001, .01, "placement unavailable") &&
              exceeded.state == ObligationState::Exceeded &&
              exceeded.reason == "source residual exceeds policy" &&
              exceeded.pathTerms.size() == 1 && exceeded.pathTerms[0] == .02 &&
              exceeded.pathTermKinds.size() == 1 &&
              exceeded.pathTermKinds[0] == "post-trim-lift-residual",
          "composition preserves an exceeded row and its reason");
    ToleranceLedger tolerance;
    tolerance.Observe(false, 0, .01);
    tolerance.Observe(true, std::numeric_limits<double>::quiet_NaN(), .01);
    tolerance.Observe(true, .001, .01);
    check(tolerance.required == 3 && tolerance.unassessed == 1 &&
              tolerance.invalid == 1 && tolerance.within == 1 &&
              !tolerance.Complete(),
          "missing and NaN tolerance observations remain visible");
    std::cout << "PASS: " << checks << " native interpretation ledger checks\n";
    return 0;
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
