#pragma once
#include <BRepGProp.hxx>
#include <GProp_GProps.hxx>
#include <BRep_Tool.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepCheck_Face.hxx>
#include <BRepCheck_Shell.hxx>
#include <BRepClass3d_SolidClassifier.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS_Iterator.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Shell.hxx>
#include <Precision.hxx>
#include <Standard_Failure.hxx>
#include <emscripten/val.h>
#include <cmath>
#include <string>
#include <vector>
#include <limits>

namespace MalievKernel { namespace RegionMeasures {
// Numerical integration policy, independent of the physical repair budget.
constexpr double AcceptanceRelativeError=0.0001;
struct Attempt {
    double requestedRelativeError, error;
    bool available;
    std::string reason;
};
struct Result {
    bool available=false;
    std::string reason="not_evaluated";
    double value=0, error=std::numeric_limits<double>::quiet_NaN();
    double requestedRelativeError=std::numeric_limits<double>::quiet_NaN();
    gp_Pnt centroid;
    std::vector<Attempt> attempts;
};
inline Result Unavailable(const char* reason,double error=std::numeric_limits<double>::quiet_NaN()) { Result r;r.reason=reason;r.error=error;return r; }
inline Result Accept(double value,const gp_Pnt& centroid,double error) {
    if(!std::isfinite(value)||!std::isfinite(centroid.X())||!std::isfinite(centroid.Y())||!std::isfinite(centroid.Z())||!std::isfinite(error)) return Unavailable("nonfinite_integration_result",error);
    if(value<=0) return Unavailable("nonpositive_mass",error);
    if(error<0) return Unavailable("invalid_error_estimate",error);
    if(error>AcceptanceRelativeError) return Unavailable("integration_target_not_met",error);
    Result r;r.available=true;r.reason.clear();r.value=value;r.centroid=centroid;r.error=error;return r;
}
// A stricter request may converge despite OCCT returning an estimate above the
// requested epsilon. Acceptance is always the same fixed numerical target.
template<typename Integrate> Result VolumeAttempts(const Integrate& integrate) {
    std::vector<Attempt> attempts;
    Result result;
    for(const double epsilon:{0.0001,0.00001,0.000001}) {
        try { result=integrate(epsilon); }
        catch(const Standard_Failure&) { result=Unavailable("native_integration_failed"); }
        result.requestedRelativeError=epsilon;
        attempts.push_back({epsilon,result.error,result.available,result.reason});
        result.attempts=attempts;
        if(result.reason!="integration_target_not_met") break;
    }
    return result;
}
inline Result Area(const TopoDS_Face& face,bool millimeters) {
    if(!millimeters) return Unavailable("millimeter_units_unverified");
    if(face.IsNull()) return Unavailable("native_face_unavailable");
    try {
        if(BRep_Tool::Surface(face).IsNull()) return Unavailable("native_surface_unavailable");
        if(face.Orientation()!=TopAbs_FORWARD&&face.Orientation()!=TopAbs_REVERSED) return Unavailable("unsupported_face_orientation");
        BRepCheck_Analyzer analyzer(face,Standard_True,Standard_False);
        BRepCheck_Face check(face);
        if(!analyzer.IsValid()||check.IntersectWires()!=BRepCheck_NoError||check.ClassifyWires()!=BRepCheck_NoError||check.OrientationOfWires()!=BRepCheck_NoError) return Unavailable("native_face_invalid");
        GProp_GProps props;
        const Standard_Real eps=AcceptanceRelativeError;
        const Standard_Real error=BRepGProp::SurfaceProperties(face,props,eps,Standard_False);
        Result result=Accept(props.Mass(),props.CentreOfMass(),error);
        result.requestedRelativeError=eps;return result;
    } catch(const Standard_Failure&) { return Unavailable("native_integration_failed"); }
}
inline Result Volume(const TopoDS_Shape& solid,bool millimeters,bool completeMembership) {
    if(!millimeters) return Unavailable("millimeter_units_unverified");
    if(solid.IsNull()||solid.ShapeType()!=TopAbs_SOLID) return Unavailable("supported_solid_unavailable");
    if(!completeMembership) return Unavailable("incomplete_face_membership");
    try {
        int shells=0,faces=0;
        for(TopoDS_Iterator it(solid);it.More();it.Next()) {
            if(it.Value().ShapeType()!=TopAbs_SHELL) return Unavailable("invalid_solid_membership");
            ++shells;
            const auto shell=TopoDS::Shell(it.Value());
            BRepCheck_Shell check(shell);
            if(!BRep_Tool::IsClosed(shell)||check.Closed()!=BRepCheck_NoError||check.Orientation()!=BRepCheck_NoError) return Unavailable("native_shell_not_closed_or_oriented");
        }
        if(shells!=1) return Unavailable("single_shell_solid_required");
        for(TopExp_Explorer ex(solid,TopAbs_FACE);ex.More();ex.Next()) {
            ++faces;
            if(BRep_Tool::Surface(TopoDS::Face(ex.Current())).IsNull()) return Unavailable("native_surface_unavailable");
        }
        if(faces==0) return Unavailable("native_faces_unavailable");
        BRepCheck_Analyzer analyzer(solid,Standard_True,Standard_False);
        if(!analyzer.IsValid()) return Unavailable("native_solid_invalid");
        BRepClass3d_SolidClassifier classifier(solid);classifier.PerformInfinitePoint(Precision::Confusion());
        if(classifier.State()!=TopAbs_OUT) return Unavailable("native_solid_not_bounded_or_oriented");
        return VolumeAttempts([&](double epsilon) {
            GProp_GProps props; // Fresh state for every adaptive attempt.
            const Standard_Real eps=epsilon;
            const Standard_Real error=BRepGProp::VolumeProperties(solid,props,eps,Standard_True,Standard_False);
            return Accept(props.Mass(),props.CentreOfMass(),error);
        });
    } catch(const Standard_Failure&) { return Unavailable("native_integration_failed"); }
}
inline emscripten::val Export(const Result& r,const std::string& bodyId,const std::string& faceId="") {
    using emscripten::val;
    const bool area=!faceId.empty();val out=val::object();
    out.set("schema",std::string("MalievNativeRegionMeasures.v1"));
    out.set("policyId",std::string("native-adaptive-mass-properties"));out.set("policyVersion",2);
    out.set("bodyId",bodyId);if(area)out.set("faceId",faceId);
    out.set("status",std::string(r.available?"available":"unavailable"));out.set("reason",r.available?val::null():val(r.reason));
    out.set("quantity",std::string(area?"area":"volume"));out.set("units",std::string(area?"mm2":"mm3"));
    out.set("unitProvenance",std::string("same-import-verified-millimeter-normalization-required"));
    out.set("coordinateSpace",std::string("import-world"));
    out.set("method",std::string(area?"BRepGProp.SurfaceProperties-adaptive":"BRepGProp.VolumeProperties-adaptive"));
    out.set("acceptanceRelativeError",AcceptanceRelativeError);
    out.set("initialRequestedRelativeError",AcceptanceRelativeError);
    out.set("requestedRelativeError",std::isfinite(r.requestedRelativeError)?val(r.requestedRelativeError):val::null());
    out.set(area?"estimatedRelativeAreaError":"estimatedRelativeVolumeError",r.available?val(r.error):val::null());
    out.set("errorSemantics",std::string("native-successive-integration-estimate-max-face"));
    out.set("centroidErrorStatus",std::string("not-estimated"));out.set("centroidAbsoluteErrorMm",val::null());
    out.set("nativeSupportRequired",true);out.set("skipShared",false);if(!area)out.set("onlyClosed",true);
    if(!area) {
        val attempts=val::array();
        for(size_t i=0;i<r.attempts.size();++i) {
            const auto& a=r.attempts[i];val attempt=val::object();
            attempt.set("requestedRelativeError",a.requestedRelativeError);
            attempt.set("status",std::string(a.available?"available":"unavailable"));
            attempt.set("reason",a.available?val::null():val(a.reason));
            attempt.set("estimatedRelativeVolumeError",std::isfinite(a.error)?val(a.error):val::null());
            attempts.set(i,attempt);
        }
        out.set("integrationAttempts",attempts);
    }
    out.set("value",r.available?val(r.value):val::null());
    val centroid=val::null();if(r.available){centroid=val::array();centroid.set(0,r.centroid.X());centroid.set(1,r.centroid.Y());centroid.set(2,r.centroid.Z());}
    out.set("centroidMm",centroid);return out;
}
} }
