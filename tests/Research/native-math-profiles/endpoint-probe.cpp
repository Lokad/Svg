// Original arcTo body: Skia f8b66b7597c4cc859d3ed190e9c6872241e6721c.
// Copyright 2015 Google Inc. BSD license in THIRD_PARTY_NOTICES.txt.
// Minimal recording/matrix adapter; no Lokad.Svg implementation is used.
#include <algorithm>
#include <bit>
#include <cmath>
#include <cstdint>
#include <iostream>
#include <string>
#include <vector>
using SkScalar = float;
constexpr float SK_ScalarPI = 3.14159265358979323846f;
constexpr float SK_ScalarHalf = .5f;
static float SkScalarAbs(float x) { return std::abs(x); }
static float SkScalarSqrt(float x) { return std::sqrt(x); }
static float SkScalarATan2(float y, float x) { return std::atan2(y, x); }
static float SkScalarTan(float x) { return std::tan(x); }
static float SkScalarCos(float x) { return std::cos(x); }
static float SkScalarFloorToScalar(float x) { return std::floor(x); }
static int SkScalarCeilToInt(float x) { return static_cast<int>(std::ceil(x)); }
static float SkScalarRoundToScalar(float x) { return std::floor(x + .5f); }
static bool SkScalarNearlyZero(float x) { return std::abs(x) <= 1.f / 4096; }
static bool SkIsFinite(float x) { return std::isfinite(x); }
static bool SkToBool(int x) { return x != 0; }
static float SkScalarSinSnapToZero(float x) { float v = std::sin(x); return std::abs(v) <= 1.f / 65536 ? 0 : v; }
static float SkScalarCosSnapToZero(float x) { float v = std::cos(x); return std::abs(v) <= 1.f / 65536 ? 0 : v; }
struct SkPoint {
    float fX, fY;
    bool operator==(const SkPoint&) const = default;
    SkPoint operator-(SkPoint p) const { return {fX-p.fX, fY-p.fY}; }
    SkPoint operator+(SkPoint p) const { return {fX+p.fX, fY+p.fY}; }
    SkPoint& operator*=(float k) { fX*=k; fY*=k; return *this; }
    SkPoint& operator-=(SkPoint p) { fX-=p.fX; fY-=p.fY; return *this; }
    SkPoint& operator+=(SkPoint p) { fX+=p.fX; fY+=p.fY; return *this; }
    void scale(float k) { *this *= k; }
    void offset(float x, float y) { fX+=x; fY+=y; }
    void set(float x, float y) { fX=x; fY=y; }
};
using SkVector=SkPoint;
struct SkMatrix {
    float a=1,b=0,c=0,d=1;
    void setRotate(float deg) { float angle=deg*(SK_ScalarPI/180);float sn=SkScalarSinSnapToZero(angle),cs=SkScalarCosSnapToZero(angle);a=cs;b=sn;c=-sn;d=cs; }
    void setScale(float x,float y) { a=x;b=0;c=0;d=y; }
    void preRotate(float deg) { SkMatrix m;m.setRotate(deg);float x=a,y=d;a=x*m.a;b=y*m.b;c=x*m.c;d=y*m.d; }
    void preScale(float x,float y) { a*=x;b*=x;c*=y;d*=y; }
    SkPoint mapPoint(SkPoint p) const { return {a*p.fX+c*p.fY,b*p.fX+d*p.fY}; }
    void mapPoints(SkPoint (&out)[2],const SkPoint (&in)[2]) const { for(int i=0;i<2;i++) out[i]=mapPoint(in[i]); }
};
enum class SkPathDirection { kCW,kCCW };
struct SkPathBuilder {
    enum ArcSize { Small,Large };
    struct Conic { SkPoint control,end;float weight; };
    std::vector<SkPoint> fPts;
    std::vector<Conic> conics;
    void ensureMove() {}
    SkPathBuilder& lineTo(SkPoint p) { fPts.push_back(p); return *this; }
    void conicTo(SkPoint control,SkPoint end,float weight) { conics.push_back({control,end,weight});fPts.push_back(control);fPts.push_back(end); }
    SkPathBuilder& arcTo(SkPoint rad,float angle,ArcSize large,SkPathDirection sweep,SkPoint end);
};
SkPathBuilder& SkPathBuilder::arcTo(SkPoint rad, SkScalar angle, SkPathBuilder::ArcSize arcLarge,
                                    SkPathDirection arcSweep, SkPoint endPt) {
    this->ensureMove();

    const SkPoint srcPts[2] = { fPts.back(), endPt };

    // If rx = 0 or ry = 0 then this arc is treated as a straight line segment (a "lineto")
    // joining the endpoints.
    // http://www.w3.org/TR/SVG/implnote.html#ArcOutOfRangeParameters
    if (!rad.fX || !rad.fY) {
        return this->lineTo(endPt);
    }
    // If the current point and target point for the arc are identical, it should be treated as a
    // zero length path. This ensures continuity in animations.
    if (srcPts[0] == srcPts[1]) {
        return this->lineTo(endPt);
    }
    SkScalar rx = SkScalarAbs(rad.fX);
    SkScalar ry = SkScalarAbs(rad.fY);
    SkVector midPointDistance = srcPts[0] - srcPts[1];
    midPointDistance *= 0.5f;

    SkMatrix pointTransform;
    pointTransform.setRotate(-angle);

    SkPoint transformedMidPoint = pointTransform.mapPoint(midPointDistance);
    SkScalar squareRx = rx * rx;
    SkScalar squareRy = ry * ry;
    SkScalar squareX = transformedMidPoint.fX * transformedMidPoint.fX;
    SkScalar squareY = transformedMidPoint.fY * transformedMidPoint.fY;

    // Check if the radii are big enough to draw the arc, scale radii if not.
    // http://www.w3.org/TR/SVG/implnote.html#ArcCorrectionOutOfRangeRadii
    SkScalar radiiScale = squareX / squareRx + squareY / squareRy;
    if (radiiScale > 1) {
        radiiScale = SkScalarSqrt(radiiScale);
        rx *= radiiScale;
        ry *= radiiScale;
    }

    pointTransform.setScale(1 / rx, 1 / ry);
    pointTransform.preRotate(-angle);

    SkPoint unitPts[2];
    pointTransform.mapPoints(unitPts, srcPts);
    SkVector delta = unitPts[1] - unitPts[0];

    SkScalar d = delta.fX * delta.fX + delta.fY * delta.fY;
    SkScalar scaleFactorSquared = std::max(1 / d - 0.25f, 0.f);

    SkScalar scaleFactor = SkScalarSqrt(scaleFactorSquared);
    if ((arcSweep == SkPathDirection::kCCW) != SkToBool(arcLarge)) {  // flipped from the original implementation
        scaleFactor = -scaleFactor;
    }
    delta.scale(scaleFactor);
    SkPoint centerPoint = unitPts[0] + unitPts[1];
    centerPoint *= 0.5f;
    centerPoint.offset(-delta.fY, delta.fX);
    unitPts[0] -= centerPoint;
    unitPts[1] -= centerPoint;
    SkScalar theta1 = SkScalarATan2(unitPts[0].fY, unitPts[0].fX);
    SkScalar theta2 = SkScalarATan2(unitPts[1].fY, unitPts[1].fX);
    SkScalar thetaArc = theta2 - theta1;
    if (thetaArc < 0 && (arcSweep == SkPathDirection::kCW)) {  // arcSweep flipped from the original implementation
        thetaArc += SK_ScalarPI * 2;
    } else if (thetaArc > 0 && (arcSweep != SkPathDirection::kCW)) {  // arcSweep flipped from the original implementation
        thetaArc -= SK_ScalarPI * 2;
    }

    // Very tiny angles cause our subsequent math to go wonky (skbug.com/40040578)
    // so we do a quick check here. The precise tolerance amount is just made up.
    // PI/million happens to fix the bug in 9272, but a larger value is probably
    // ok too.
    if (SkScalarAbs(thetaArc) < (SK_ScalarPI / (1000 * 1000))) {
        return this->lineTo(endPt);
    }

    pointTransform.setRotate(angle);
    pointTransform.preScale(rx, ry);

    // the arc may be slightly bigger than 1/4 circle, so allow up to 1/3rd
    int segments = SkScalarCeilToInt(SkScalarAbs(thetaArc / (2 * SK_ScalarPI / 3)));
    SkScalar thetaWidth = thetaArc / segments;
    SkScalar t = SkScalarTan(0.5f * thetaWidth);
    if (!SkIsFinite(t)) {
        return *this;
    }
    SkScalar startTheta = theta1;
    SkScalar w = SkScalarSqrt(SK_ScalarHalf + SkScalarCos(thetaWidth) * SK_ScalarHalf);
    auto scalar_is_integer = [](SkScalar scalar) -> bool {
        return scalar == SkScalarFloorToScalar(scalar);
    };
    bool expectIntegers = SkScalarNearlyZero(SK_ScalarPI/2 - SkScalarAbs(thetaWidth)) &&
        scalar_is_integer(rx) && scalar_is_integer(ry) &&
        scalar_is_integer(endPt.fX) && scalar_is_integer(endPt.fY);

    for (int i = 0; i < segments; ++i) {
        SkScalar endTheta    = startTheta + thetaWidth,
                 sinEndTheta = SkScalarSinSnapToZero(endTheta),
                 cosEndTheta = SkScalarCosSnapToZero(endTheta);

        unitPts[1].set(cosEndTheta, sinEndTheta);
        unitPts[1] += centerPoint;
        unitPts[0] = unitPts[1];
        unitPts[0].offset(t * sinEndTheta, -t * cosEndTheta);
        SkPoint mapped[2];
        pointTransform.mapPoints(mapped, unitPts);
        /*
        Computing the arc width introduces rounding errors that cause arcs to start
        outside their marks. A round rect may lose convexity as a result. If the input
        values are on integers, place the conic on integers as well.
         */
        if (expectIntegers) {
            for (SkPoint& point : mapped) {
                point.fX = SkScalarRoundToScalar(point.fX);
                point.fY = SkScalarRoundToScalar(point.fY);
            }
        }
        this->conicTo(mapped[0], mapped[1], w);
        startTheta = endTheta;
    }

    // The final point should match the input point (by definition); replace it to
    // ensure that rounding errors in the above math don't cause any problems.
    fPts.back() = endPt;
    return *this;
}
int main() {
    std::cout << "id,index,point,xBits,yBits,weightBits\n";
    std::string id; float p[9];
    while(std::cin >> id) {
        for(float &v:p) if(!(std::cin>>v)) return 2;
        SkPathBuilder builder;builder.fPts.push_back({p[0],p[1]});
        builder.arcTo({p[2],p[3]},p[4],p[5]!=0?SkPathBuilder::Large:SkPathBuilder::Small,p[6]!=0?SkPathDirection::kCW:SkPathDirection::kCCW,{p[7],p[8]});
        if(!builder.conics.empty()) builder.conics.back().end=builder.fPts.back();
        int index=0;
        for(const auto &conic:builder.conics) {
            int point=0;
            for(SkPoint v:{conic.control,conic.end}) std::cout << id << ',' << index << ',' << point++ << ',' << std::bit_cast<uint32_t>(v.fX) << ',' << std::bit_cast<uint32_t>(v.fY) << ',' << std::bit_cast<uint32_t>(conic.weight) << '\n';
            ++index;
        }
    }
}
