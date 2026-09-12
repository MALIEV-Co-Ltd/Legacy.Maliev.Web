#pragma once
#include "kernel-repair-policy.hpp"
#include <stdexcept>
#include <string>
#include <vector>

namespace MalievNativeInterpretation {
enum class ObligationState { Unassessed, Bounded, Exceeded, Unavailable };
struct Obligation {
  std::string kind, occurrence, reason = "not assessed";
  std::string referencePath = "not-applicable", metricMethod = "not-applicable";
  std::string sourceFaceItemId, sourceEdgeItemId, faceId, wireId, coedgeId,
      edgeId, operationId, sourceVertexItemId, connectorProofId,
      associationStatus = "not-applicable";
  int sourceOrientation = 0;
  double sourceFirst = 0, sourceLast = 0;
  double priorFirst = 0, priorLast = 0;
  bool priorRangeCaptured = false;
  ObligationState state = ObligationState::Unassessed;
  std::vector<double> pathTerms;
  std::vector<std::string> pathTermKinds;
  double upper = std::numeric_limits<double>::infinity();
  bool finalized = false;
};
struct LedgerCounts {
  size_t required = 0, evaluated = 0, unassessed = 0;
  size_t bounded = 0, exceeds = 0, unavailable = 0;
};
inline bool AppendBoundedPathTerm(Obligation &row, double term, double budget,
                                  const std::string &unavailableReason) {
  // Composition is only meaningful after the underlying evaluator produced a
  // bound.  In particular, do not turn deadline-skipped or already-exceeded
  // work into an evaluated/unavailable row during serialization.
  if (row.state != ObligationState::Bounded)
    return false;
  if (!std::isfinite(term) || term < 0 || !std::isfinite(row.upper) ||
      row.upper < 0) {
    row.state = ObligationState::Unavailable;
    row.reason = unavailableReason;
    return false;
  }
  row.pathTerms.push_back(term);
  row.pathTermKinds.push_back("periodic-world-displacement");
  row.upper = MalievRepair::ComposePathUpper(row.upper, term);
  if (!std::isfinite(budget) || budget <= 0 || row.upper > budget) {
    row.state = ObligationState::Unavailable;
    row.reason = "composed path upper exceeds policy";
    return false;
  }
  return true;
}
class RequiredLedger {
public:
  std::vector<Obligation> rows;
  size_t Require(const std::string &kind, const std::string &occurrence) {
    if (kind.empty() || occurrence.empty())
      throw std::invalid_argument("missing obligation identity");
    for (const auto &row : rows)
      if (row.kind == kind && row.occurrence == occurrence)
        throw std::invalid_argument("duplicate obligation occurrence");
    Obligation row;
    row.kind = kind;
    row.occurrence = occurrence;
    rows.push_back(row);
    return rows.size() - 1;
  }
  void Finish(size_t index, bool attempted, const std::string &status,
              const std::vector<double> &terms, double budget,
              const std::string &reason = "") {
    Obligation &row = rows.at(index);
    if (row.finalized)
      throw std::logic_error("obligation already finalized");
    row.finalized = true;
    row.reason = reason;
    row.pathTerms = terms;
    if (!attempted) {
      row.reason = reason.empty() ? "required evaluator not attempted" : reason;
      return;
    }
    row.state = ObligationState::Unavailable;
    if (status == "exceeds-budget") {
      row.state = ObligationState::Exceeded;
      return;
    }
    if (status != "bounded-within-budget") {
      if (row.reason.empty())
        row.reason = "evaluator did not establish a bound";
      return;
    }
    if (!std::isfinite(budget) || budget <= 0 || terms.empty()) {
      row.reason = "missing finite positive policy or path terms";
      return;
    }
    for (const double term : terms)
      if (!std::isfinite(term) || term < 0) {
        row.reason = "nonfinite or negative path term";
        return;
      }
    row.upper = terms.front();
    for (size_t i = 1; i < terms.size(); ++i)
      row.upper = MalievRepair::ComposePathUpper(row.upper, terms[i]);
    if (!std::isfinite(row.upper) || row.upper > budget) {
      row.reason = "composed path upper exceeds policy";
      return;
    }
    row.state = ObligationState::Bounded;
  }
  LedgerCounts Counts() const {
    LedgerCounts counts;
    counts.required = rows.size();
    for (const auto &row : rows) {
      if (row.state == ObligationState::Unassessed)
        ++counts.unassessed;
      else {
        ++counts.evaluated;
        if (row.state == ObligationState::Bounded)
          ++counts.bounded;
        else if (row.state == ObligationState::Exceeded)
          ++counts.exceeds;
        else
          ++counts.unavailable;
      }
    }
    return counts;
  }
  bool Complete() const {
    const auto counts = Counts();
    return counts.required > 0 && counts.required == counts.bounded;
  }
};
struct ToleranceLedger {
  size_t required = 0, evaluated = 0, unassessed = 0;
  size_t within = 0, exceeds = 0, invalid = 0;
  double maximum = 0;
  void Observe(bool captured, double value, double budget) {
    ++required;
    if (!captured) {
      ++unassessed;
      return;
    }
    ++evaluated;
    if (!std::isfinite(value) || value < 0 || !std::isfinite(budget) ||
        budget <= 0) {
      ++invalid;
      return;
    }
    maximum = std::max(maximum, value);
    if (value > budget)
      ++exceeds;
    else
      ++within;
  }
  bool Complete() const { return required > 0 && required == within; }
};
} // namespace MalievNativeInterpretation
