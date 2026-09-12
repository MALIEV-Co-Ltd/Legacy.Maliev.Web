#pragma once
// MALIEV extension to occt-import-js, LGPL-2.1 (see upstream LICENSE.md).
#include "importer-utils.hpp"
#include "kernel-circular-revolution.hpp"
#include <BRepAdaptor_Surface.hxx>
#include <BRepBndLib.hxx>
#include <Bnd_Box.hxx>
#include <gp_Pln.hxx>
#include <gp_Cylinder.hxx>
#include <gp_Cone.hxx>
#include <gp_Sphere.hxx>
#include <gp_Torus.hxx>
#include <Standard_Failure.hxx>
#include <TopExp_Explorer.hxx>
#include <Poly_Triangulation.hxx>
#include <emscripten/val.h>
#include "kernel-region-measures.hpp"

namespace MalievKernel {
using emscripten::val;
template <typename T> val Triple(const T& p) {
    val a = val::array(); a.set(0, p.X()); a.set(1, p.Y()); a.set(2, p.Z()); return a;
}
inline void Frame(val& o, const gp_Ax3& frame) {
    o.set("origin", Triple(frame.Location()));
    o.set("axis", Triple(frame.Direction()));
    o.set("xDirection", Triple(frame.XDirection()));
    o.set("yDirection", Triple(frame.YDirection()));
    o.set("direct", frame.Direct());
}
inline void RevolutionRecord(val& o,const MalievCircularRevolution::CircleRevolution& r) {
    o.set("type",std::string("circular_revolution"));
    o.set("origin",Triple(r.axis.Location())); o.set("axis",Triple(r.axis.Direction()));
    val circle=val::object(); circle.set("status",std::string("exact")); circle.set("type",std::string("circle"));
    circle.set("origin",Triple(r.circle.Location())); circle.set("xDirection",Triple(r.circle.XAxis().Direction()));
    circle.set("yDirection",Triple(r.circle.YAxis().Direction())); circle.set("radius",r.circle.Radius());
    o.set("basisCircle",circle);
}
inline val Unavailable(const char* reason) {
    val o = val::object(); o.set("status", std::string("unavailable"));
    o.set("reason", std::string(reason)); return o;
}
}
#include "kernel-trims.hpp"
namespace MalievKernel {
inline void WriteFace(const Face& source, val& output, int bodyIndex, int faceIndex, ExportContext& context, bool millimeters=false) {
    const std::string bodyId = "body-" + std::to_string(bodyIndex);
    output.set("bodyId", bodyId);
    output.set("faceId", bodyId + "/face-" + std::to_string(faceIndex));
    const std::string faceId=bodyId+"/face-"+std::to_string(faceIndex);
    output.set("nativeRegionMeasures",RegionMeasures::Export(RegionMeasures::Unavailable("native_face_unavailable"),bodyId,faceId));
    output.set("nativeRotationalBand",RotationalBand::Export(RotationalBand::Result(),bodyId,faceId));
    output.set("trims", Unavailable("ordered_wires_and_pcurves_not_exported"));
    output.set("adjacency", Unavailable("source_edge_ownership_not_exported"));
    const OcctFace* occtFace = dynamic_cast<const OcctFace*>(&source);
    if (!occtFace) {
        context.complete = false;
        output.set("support", Unavailable("not_an_occt_face")); return;
    }
    try {
    const TopoDS_Face& face = occtFace->KernelFace();
    output.set("nativeRegionMeasures",RegionMeasures::Export(RegionMeasures::Area(face,millimeters),bodyId,faceId));
    output.set("orientation", static_cast<int>(face.Orientation()));
    output.set("orientationStatus", std::string(
        face.Orientation() == TopAbs_FORWARD || face.Orientation() == TopAbs_REVERSED
        ? "resolved" : "unsupported_internal_or_external"));
    output.set("assemblyInstance", Unavailable("instance_hierarchy_not_exported"));
    val placement = val::array();
    const gp_Trsf trsf = face.Location().Transformation();
    for (int row = 1; row <= 3; ++row)
        for (int col = 1; col <= 4; ++col) placement.set((row-1)*4+col-1, trsf.Value(row,col));
    output.set("placement3x4", placement);
    Bnd_Box box;
    BRepBndLib::AddOptimal(face, box, Standard_False, Standard_False);
    if (!box.IsVoid() && !box.IsOpen()) {
        Standard_Real x0,y0,z0,x1,y1,z1; box.Get(x0,y0,z0,x1,y1,z1);
        val bounds = val::object(); bounds.set("min", Triple(gp_Pnt(x0,y0,z0)));
        bounds.set("max", Triple(gp_Pnt(x1,y1,z1)));
        bounds.set("method", std::string("BRepBndLib.AddOptimal-without-triangulation"));
        output.set("bounds", bounds);
    } else output.set("bounds", Unavailable("void_or_unbounded_face"));
    BRepAdaptor_Surface surface(face, Standard_True);
    // OCCT shares edge nodes between faces; imported topology tolerances are
    // not a promise that those nodes lie exactly on each analytic support.
    val precision = val::object();
    const double faceTolerance = BRep_Tool::Tolerance(face);
    double edgeTolerance = 0.0, vertexTolerance = 0.0;
    for (TopExp_Explorer e(face, TopAbs_EDGE); e.More(); e.Next())
        edgeTolerance = std::max(edgeTolerance, BRep_Tool::Tolerance(TopoDS::Edge(e.Current())));
    for (TopExp_Explorer v(face, TopAbs_VERTEX); v.More(); v.Next())
        vertexTolerance = std::max(vertexTolerance, BRep_Tool::Tolerance(TopoDS::Vertex(v.Current())));
    precision.set("faceTolerance", faceTolerance);
    precision.set("maxEdgeTolerance", edgeTolerance);
    precision.set("maxVertexTolerance", vertexTolerance);
    precision.set("maxTopologyTolerance", std::max(faceTolerance, std::max(edgeTolerance, vertexTolerance)));
    precision.set("coordinateSpace", std::string("import-world"));
    if (!box.IsVoid() && !box.IsOpen()) {
        Bnd_Box toleranceBox = box;
        toleranceBox.Enlarge(std::max(faceTolerance, std::max(edgeTolerance, vertexTolerance)));
        Standard_Real x0,y0,z0,x1,y1,z1;
        toleranceBox.Get(x0,y0,z0,x1,y1,z1);
        val bounds = val::object();
        bounds.set("min", Triple(gp_Pnt(x0,y0,z0)));
        bounds.set("max", Triple(gp_Pnt(x1,y1,z1)));
        bounds.set("method", std::string("exact-bounds-expanded-by-native-topology-tolerance"));
        output.set("toleranceBounds", bounds);
    } else output.set("toleranceBounds", Unavailable("void_or_unbounded_face"));
    TopLoc_Location meshLocation;
    Handle(Poly_Triangulation) triangulation = BRep_Tool::Triangulation(face, meshLocation);
    if (!triangulation.IsNull() && triangulation->HasUVNodes()) {
        double maxDeviation = 0.0;
        for (int i = 1; i <= triangulation->NbNodes(); ++i) {
            const gp_Pnt node = triangulation->Node(i).Transformed(meshLocation.Transformation());
            const gp_Pnt2d uv = triangulation->UVNode(i);
            maxDeviation = std::max(maxDeviation, node.Distance(surface.Value(uv.X(), uv.Y())));
        }
        precision.set("status", std::string("available"));
        precision.set("triangulationDeflection", triangulation->Deflection());
        precision.set("maxNodeSurfaceDeviation", maxDeviation);
        precision.set("deviationMethod", std::string("node-to-surface-at-native-uv"));
    } else {
        precision.set("status", std::string("unavailable"));
        precision.set("reason", std::string("triangulation_or_uv_nodes_missing"));
    }
    output.set("precision", precision);
    val support = val::object();
    support.set("coordinateSpace", std::string("import-world"));
    support.set("occtType", static_cast<int>(surface.GetType()));
    if (face.Orientation() == TopAbs_FORWARD || face.Orientation() == TopAbs_REVERSED)
        support.set("orientationSign", face.Orientation() == TopAbs_REVERSED ? -1 : 1);
    else support.set("orientationSign", val::null());
    switch (surface.GetType()) {
    case GeomAbs_Plane: {
        gp_Pln p = surface.Plane(); support.set("type", std::string("plane")); Frame(support,p.Position());
        gp_Dir normal = p.Position().XDirection().Crossed(p.Position().YDirection());
        if (face.Orientation() == TopAbs_REVERSED) normal.Reverse();
        if (face.Orientation() == TopAbs_FORWARD || face.Orientation() == TopAbs_REVERSED)
            support.set("orientedNormal", Triple(normal));
        else support.set("orientedNormal", val::null());
        break;
    }
    case GeomAbs_Cylinder: {
        gp_Cylinder p = surface.Cylinder(); support.set("type", std::string("cylinder"));
        Frame(support,p.Position()); support.set("radius",p.Radius()); break;
    }
    case GeomAbs_Cone: {
        gp_Cone p = surface.Cone(); support.set("type", std::string("cone"));
        Frame(support,p.Position()); support.set("referenceRadius",p.RefRadius());
        support.set("semiAngleRadians",p.SemiAngle()); break;
    }
    case GeomAbs_Sphere: {
        gp_Sphere p = surface.Sphere(); support.set("type", std::string("sphere"));
        Frame(support,p.Position()); support.set("radius",p.Radius()); break;
    }
    case GeomAbs_Torus: {
        gp_Torus p = surface.Torus(); support.set("type", std::string("torus"));
        Frame(support,p.Position()); support.set("majorRadius",p.MajorRadius());
        support.set("minorRadius",p.MinorRadius()); break;
    }
    case GeomAbs_SurfaceOfRevolution: {
        MalievCircularRevolution::CircleRevolution r;
        if (MalievCircularRevolution::Read(surface,r)) RevolutionRecord(support,r);
        else { support.set("type",std::string("unsupported")); support.set("reason",std::string("unsupported_revolution_basis")); }
        break;
    }
    default:
        support.set("type", std::string("unsupported"));
        support.set("reason", std::string("non_analytic_surface"));
    }
    output.set("support",support);
    } catch (const Standard_Failure&) {
        output.set("support", Unavailable("kernel_surface_export_failed"));
        output.set("bounds", Unavailable("kernel_surface_export_failed"));
    }
    context.WriteTrims(occtFace->KernelFace(), output,millimeters);
}
}
