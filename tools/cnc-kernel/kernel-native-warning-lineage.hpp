#pragma once
#include <Interface_Check.hxx>
#include <TopoDS_Shape.hxx>
#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace MalievNativeWarning {
using EmissionToken = std::uint64_t;
struct Emission {
  std::string kind;
  size_t operation = 0;
};
struct Delivery {
  EmissionToken emission = 0;
  int sourceEntity = 0;
  Handle(Standard_Transient) model;
  Handle(Interface_Check) check;
  Handle(TCollection_HAsciiString) warning;
  TopoDS_Shape originalTarget, finalTarget;
  std::string sourceIdentityReason;
};
struct State {
  bool enabled = false;
  std::uint64_t generation = 0;
  size_t resourceFailures = 0, appendFailures = 0, sourceIdentityFailures = 0;
  std::map<const void *, EmissionToken> messages;
  std::vector<Emission> emissions;
  std::vector<Delivery> deliveries;
};
inline State &Store() {
  static State state;
  return state;
}
inline void Reset(bool enabled) {
  auto &state = Store();
  const auto generation = state.generation + 1;
  state = State();
  state.enabled = enabled;
  state.generation = generation;
}
inline void Constructed(const void *message) {
  Store().messages.erase(message);
}
inline EmissionToken Token(const void *message) {
  const auto &state = Store();
  if (!state.enabled)
    return 0;
  const auto entry = state.messages.find(message);
  return entry == state.messages.end() ? 0 : entry->second;
}
inline void Copied(const void *destination, const void *source) {
  const auto token =
      Token(source); // Read before invalidating a reused address.
  Constructed(destination);
  if (!token)
    return;
  auto &state = Store();
  if (state.messages.size() >= 65536) {
    ++state.resourceFailures;
    return;
  }
  state.messages[destination] = token;
}
inline EmissionToken NewEmission(const std::string &kind, size_t operation) {
  auto &state = Store();
  if (!state.enabled)
    return 0;
  if (state.emissions.size() >= 65536 || state.generation > 0xffffffffULL) {
    ++state.resourceFailures;
    return 0;
  }
  Emission emission;
  emission.kind = kind;
  emission.operation = operation;
  state.emissions.push_back(emission);
  return (state.generation << 32) | state.emissions.size();
}
inline int EmissionIndex(EmissionToken token) {
  const auto &state = Store();
  const auto index = token & 0xffffffffULL;
  if (!state.enabled || (token >> 32) != state.generation || index == 0 ||
      index > state.emissions.size())
    return -1;
  return static_cast<int>(index - 1);
}
inline EmissionToken Tag(const void *message, const std::string &kind,
                         size_t operation) {
  Constructed(message);
  const auto token = NewEmission(kind, operation);
  if (!token)
    return 0;
  auto &state = Store();
  if (state.messages.size() >= 65536) {
    ++state.resourceFailures;
    return 0;
  }
  state.messages[message] = token;
  return token;
}
inline void Appended(const Handle(Interface_Check) & check, int before,
                     EmissionToken token, int entity,
                     const Handle(Standard_Transient) & model,
                     const TopoDS_Shape &original = TopoDS_Shape(),
                     const TopoDS_Shape &final = TopoDS_Shape()) {
  auto &state = Store();
  if (!state.enabled)
    return;
  if (check.IsNull() || check->NbWarnings() != before + 1 || before < 0) {
    ++state.appendFailures;
    return;
  }
  if (state.deliveries.size() >= 65536) {
    ++state.resourceFailures;
    return;
  }
  Delivery delivery;
  const bool sourceBound = !model.IsNull() && entity > 0;
  if (!sourceBound) {
    ++state.sourceIdentityFailures;
    delivery.sourceIdentityReason =
        model.IsNull() ? "missing-source-model" : "unresolved-source-entity";
  }
  delivery.emission = sourceBound && EmissionIndex(token) >= 0 ? token : 0;
  delivery.sourceEntity = entity;
  delivery.model = model;
  delivery.check = check;
  delivery.warning = check->Warning(before + 1, Standard_True);
  delivery.originalTarget = original;
  delivery.finalTarget = final;
  state.deliveries.push_back(delivery);
}
inline int FindDelivery(const Handle(Interface_Check) & check,
                        const Handle(TCollection_HAsciiString) & warning,
                        int entity, const Handle(Standard_Transient) & model) {
  int found = -1;
  const auto &state = Store();
  if (!state.enabled || model.IsNull() || entity <= 0)
    return -1;
  for (size_t i = 0; i < state.deliveries.size(); ++i) {
    const auto &delivery = state.deliveries[i];
    if (delivery.check == check && delivery.warning == warning &&
        delivery.sourceEntity == entity && delivery.model == model) {
      if (found >= 0)
        return -2;
      found = static_cast<int>(i);
    }
  }
  return found;
}
} // namespace MalievNativeWarning
