﻿/*
DOCUMENTATION- phil, 12/16/19
This is the stateful wrapper around ThermoMath.
It represents a "current" state of water. After initialization, the state must always remain consistent.
For this reason, the API consists of applying deltas to some assumed consistent state.
It should be safe to assume that after any method call, the state remains consistent.

This is also responsible for applying itself visually to the game objects in the scene, (ie, position of the piston, % of water/steam, etc...) including generating the 3d graph
(^ this could be abstracted out into _another_ wrapper, but I don't see the value added [this is not a general-purpose thermomathstate class; it is built for this one VR application])
*/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

//One-Off class used for ordering points in graphgen zipper phase
class GRAPHPTCMP : IComparer<int>
{
  public List<Vector3> mesh_positions;
  public GRAPHPTCMP(List<Vector3> _mesh_positions)
  {
    mesh_positions = _mesh_positions;
  }

  public int Compare(int ai, int bi)
  {
    Vector3 a = mesh_positions[ai];
    Vector3 b = mesh_positions[bi];
    if(a.y > b.y) return 1;
    if(a.y < b.y) return -1;
    if(a.z > b.z) return 1;
    if(a.z < b.z) return -1;
    return 0;
  }
}

public class ThermoState : MonoBehaviour
{
  //xyz corresponds to vpt (Y = "up")
  int samples = 350;

  //state
  public double pressure; //pascals
  public double temperature; //kalvin
  public double volume; //M^3/kg
  public double internalenergy; //J/kg
  public double entropy; //J/kgK
  public double enthalpy; //J/kg
  public double quality; //%
  double prev_pressure;
  double prev_temperature;
  double prev_volume;
  double prev_internalenergy;
  double prev_entropy;
  double prev_enthalpy;
  double prev_quality;

  //static properties of system
  public double mass = 1; //kg
  public double radius = 0.05; //M
  //public double surfacearea = Math.Pow(3.141592*radius,2.0); //M^2 //hardcoded answer below
  public double surfacearea = 0.024674011; //M^2 //hardcoded answer to eqn above
  public double surfacearea_insqr = 38.2447935395871; //in^2 //hardcoded conversion from m^2 to in^2

  //vessel
  GameObject vessel;
  GameObject container;
  GameObject piston;
  float piston_min_y;
  float piston_max_y;
  GameObject contents;
  float contents_min_h; //h = "height", not "enthalpy"
  float contents_max_h; //h = "height", not "enthalpy"
  GameObject water;
  GameObject steam;

  //mesh
  GameObject graph;
  GameObject state_dot;
  public Material graph_material;
  TextMeshPro text_pressure;
  TextMeshPro text_temperature;
  TextMeshPro text_volume;
  TextMeshPro text_internalenergy;
  TextMeshPro text_entropy;
  TextMeshPro text_enthalpy;
  TextMeshPro text_quality;

  void Awake()
  {
    ThermoMath.Init();
  }

  // Start is called before the first frame update
  void Start()
  {
    //(these are just used to detect editor deltas on a frame boundary)
    sample_lbase_prev = sample_lbase;
    plot_lbase_prev = plot_lbase;

    findObjects();
    genMesh();
    reset_state();
    transform_to_state();
  }

  //sample bias- "graph density"
  [Range(0.001f,20)]
  public double sample_lbase = 1.6f;
  double sample_lbase_prev = 0f;
  double sample(double t) { return Math.Pow(t,sample_lbase); }

  //plot bias- "graph zoom"
  [Range(0.001f,10)]
  public double plot_lbase = 10f;
  double plot_lbase_prev = 0f;
  public float plot_dimension(double min, double max, double val) { double lval = Math.Log(val,plot_lbase); double lmax = Math.Log(max,plot_lbase); double lmin = Math.Log(min,plot_lbase); return (float)((lval-lmin)/(lmax-lmin)); }

  public Vector3 plot(double pressure, double volume, double temperature)
  {
    float pplot = plot_dimension(ThermoMath.p_min,ThermoMath.p_max,pressure);
    float vplot = plot_dimension(ThermoMath.v_min,ThermoMath.v_max,volume);
    float tplot = plot_dimension(ThermoMath.t_min,ThermoMath.t_max,temperature);
    return new Vector3(vplot,pplot,tplot);
  }

  //generates points from thermomath api, and stitches them together into a mesh
  //the "only reason" this is complex is:
  // we generate a "biased", "zoomed" grid of the mesh looked at from one axis ("looking at yz graph").
  // then we stitch this uniform (uniform other than bias/zoom, which can be "ignored") graph together.
  // however, there is a region of the generated graph ("the vapor dome") which is "constant z" (so invisible to this perspective).
  // so we detect triangles that span this "invisible" region, and cut them out of the stitching.
  // we then generate the vapor dome points _independently_, and create a very nice mesh of the region across the "xy" plane, which by design fits right into the cutaway stitching.
  // the final step then, is to "zip" together the two meshes.
  // this is done by walking the sorted list of "orphaned" points (<- I could have come up with a better name for that...), which corresponds to the list of points disconnected by the cutting of the grid mesh
  // and simultaneously walking the sorted list of the vapor dome region points, zig-zagging triangles to fill the space
  //the good news: any complexity from the generation of the mesh is pretty well isolated to this one function
  void genMesh()
  {
    GameObject old_gm = GameObject.Find("graph_mesh");
    if(old_gm != null) Destroy(old_gm);

    int n_pts = samples*samples;
    int n_pts_per_group = 1000;
    int n_groups = (int)Mathf.Ceil(n_pts / n_pts_per_group);

    Vector3[] pt_positions;

    //gen positions
    pt_positions = new Vector3[n_pts];
    for(int y = 0; y < samples; y++)
    {
      double pt = ((double)y/(samples-1));
      for(int z = 0; z < samples; z++)
      {
        double tt = ((double)z/(samples-1));
        double pst = sample(pt);
        double tst = sample(tt);
        double p = ThermoMath.p_given_percent(pst);
        double t = ThermoMath.t_given_percent(tst);
        double v = ThermoMath.v_given_pt(p,t);
        //pvt in Pa, M^3/Kg, K

        //Debug.LogFormat("p:{0}Pa, v:{1}M^3/Kg, t:{2}K",p,v,t);
        int i = samples*y+z;
        pt_positions[i] = plot(p,v,t);
      }
    }

    //MESH
    List<Vector3> mesh_positions;
    List<Vector3> mesh_normals;
    List<int> mesh_triangles;

    mesh_positions = new List<Vector3>(pt_positions);

    int vi = 0;
    int ni = 0;
    mesh_triangles = new List<int>((samples-1)*(samples-1)*6);
    for(int y = 0; y < samples-1; y++)
    {
      for(int z = 0; z < samples-1; z++)
      {
        vi = samples*y+z;
        mesh_triangles.Add(vi        +0); ni++;
        mesh_triangles.Add(vi+samples+0); ni++;
        mesh_triangles.Add(vi+samples+1); ni++;
        mesh_triangles.Add(vi        +0); ni++;
        mesh_triangles.Add(vi+samples+1); ni++;
        mesh_triangles.Add(vi        +1); ni++;
      }
    }

    int concentrated_samples = samples*2;
    int position_dome_region = mesh_positions.Count;
    float highest_y = 0f;
    int highest_y_i = 0;
    for(int y = 0; y < concentrated_samples; y++)
    {
      double pt = ((double)y/(concentrated_samples-1));
      double pst = sample(pt);
      double p = ThermoMath.psat_given_percent(pst);
      double t = ThermoMath.tsat_given_p(p);
      //pvt in Pa, M^3/Kg, K

      //Debug.LogFormat("p:{0}Pa, v:{1}M^3/Kg, t:{2}K",p,v,t);
      float pplot = plot_dimension(ThermoMath.p_min,ThermoMath.p_max,p);
      if(pplot > highest_y) { highest_y = pplot; highest_y_i = mesh_positions.Count; }
      float tplot = plot_dimension(ThermoMath.t_min,ThermoMath.t_max,t);

      double v;
      float vplot;
      Vector3 point;

      v = ThermoMath.vliq_given_p(p);
      vplot = plot_dimension(ThermoMath.v_min,ThermoMath.v_max,v);
      point = new Vector3(vplot,pplot,tplot);
      mesh_positions.Add(point);

      v = ThermoMath.vvap_given_p(p);
      vplot = plot_dimension(ThermoMath.v_min,ThermoMath.v_max,v);
      point = new Vector3(vplot,pplot,tplot);
      mesh_positions.Add(point);
    }
    highest_y = Mathf.Lerp(highest_y,1f,0.01f); //extra nudge up

    //kill spanning triangles; gather orphans
    //"ladder"/"rung" terminology a bit arbitrary- attempts to keep track of each side of a "zipper" for each seam ("left" seam, "right" seam, each have own ladder/rung)
    List<int> left_orphans = new List<int>();
    List<int> right_orphans = new List<int>();
    int left_ladder_i = position_dome_region;
    Vector3 left_ladder = mesh_positions[left_ladder_i];
    Vector3 left_rung = mesh_positions[left_ladder_i+2];
    int right_ladder_i = left_ladder_i+1;
    Vector3 right_ladder = mesh_positions[right_ladder_i];
    Vector3 right_rung = mesh_positions[right_ladder_i+2];
    for(var i = 0; i < mesh_triangles.Count; i+=3)
    {
      int ai = mesh_triangles[i+0];
      int bi = mesh_triangles[i+1];
      int ci = mesh_triangles[i+2];
      Vector3 a = mesh_positions[ai];
      Vector3 b = mesh_positions[bi];
      Vector3 c = mesh_positions[ci];

      if((left_rung.y  < a.y || left_rung.y  < b.y || left_rung.y  < c.y) && left_ladder_i+4  < mesh_positions.Count) { left_ladder_i  += 2; left_ladder  = mesh_positions[left_ladder_i];  left_rung  = mesh_positions[left_ladder_i+2];  }
      if((right_rung.y < a.y || right_rung.y < b.y || right_rung.y < c.y) && right_ladder_i+4 < mesh_positions.Count) { right_ladder_i += 2; right_ladder = mesh_positions[right_ladder_i]; right_rung = mesh_positions[right_ladder_i+2]; }

      float x_cmp = (left_ladder.x+right_ladder.x)/2f;
      if(
        (a.y < highest_y || b.y < highest_y || c.y < highest_y) &&
        (a.x < x_cmp || b.x < x_cmp || c.x < x_cmp) &&
        (a.x > x_cmp || b.x > x_cmp || c.x > x_cmp)
      )
      {
        mesh_triangles.RemoveAt(i+2);
        mesh_triangles.RemoveAt(i+1);
        mesh_triangles.RemoveAt(i+0);
        i -= 3;

        if(a.x < x_cmp && b.x < x_cmp)
        {
          left_orphans.Add(ai);
          left_orphans.Add(bi);
          right_orphans.Add(ci);
        }
        else if(b.x < x_cmp && c.x < x_cmp)
        {
          left_orphans.Add(bi);
          left_orphans.Add(ci);
          right_orphans.Add(ai);
        }
        else if(c.x < x_cmp && a.x < x_cmp)
        {
          left_orphans.Add(ci);
          left_orphans.Add(ai);
          right_orphans.Add(bi);
        }
        else if(a.x < x_cmp)
        {
          right_orphans.Add(bi);
          right_orphans.Add(ci);
          left_orphans.Add(ai);
        }
        else if(b.x < x_cmp)
        {
          right_orphans.Add(ai);
          right_orphans.Add(ci);
          left_orphans.Add(bi);
        }
        else if(c.x < x_cmp)
        {
          right_orphans.Add(ai);
          right_orphans.Add(bi);
          left_orphans.Add(ci);
        }
        else
        {
          Debug.Log("NOOOO");
        }
      }
    }

    //sort orphans
    GRAPHPTCMP cmp = new GRAPHPTCMP(mesh_positions);

    left_orphans.Sort(cmp);
    for(int i = 1; i < left_orphans.Count; i++)
    { if(left_orphans[i-1] == left_orphans[i]) { left_orphans.RemoveAt(i); i--; } }

    right_orphans.Sort(cmp);
    for(int i = 1; i < right_orphans.Count; i++)
    { if(right_orphans[i-1] == right_orphans[i]) { right_orphans.RemoveAt(i); i--; } }

    //stitch orphans
    int left_orphan_i = 0;
    int right_orphan_i = 0;
    {
      int triangle_stitch_region = mesh_triangles.Count;
      List<int> orphans;
      int ladder_i;
      Vector3 ladder;
      Vector3 rung;
      int orphan_i;
      Vector3 orphan;
      Vector3 orung;
      int ai = 0;
      int bi = 0;
      int ci = 0;

      //left
      orphans = left_orphans;
      orphan_i = 0;
      orphan = mesh_positions[orphans[orphan_i]];
      ladder_i = position_dome_region;
      ladder = mesh_positions[ladder_i];
      rung = mesh_positions[ladder_i+2];
      mesh_triangles.Add(ladder_i);
      mesh_triangles.Add(orphans[orphan_i]);
      orphan_i++;
      orphan = mesh_positions[orphans[orphan_i]];
      orung = mesh_positions[orphans[orphan_i+1]];
      mesh_triangles.Add(orphans[orphan_i]);
      orphan = mesh_positions[orphans[orphan_i]];
      while(ladder_i+2 < mesh_positions.Count)
      {
        while(orung.z <= rung.z && orung.y <= rung.y && orphan_i+1 < orphans.Count)
        { //increment orphan
          ai = ladder_i;
          bi = orphans[orphan_i];
          ci = orphans[orphan_i+1];
          mesh_triangles.Add(ai);
          mesh_triangles.Add(bi);
          mesh_triangles.Add(ci);

          orphan_i++;
          orphan = mesh_positions[orphans[orphan_i]];
          if(orphan_i+1 < orphans.Count) orung = mesh_positions[orphans[orphan_i+1]]; //yes, both this AND previous line need +1 (+1 for advance, +1 for orung)
        }
        if(ladder_i+2 < mesh_positions.Count)
        { //increment ladder
          ai = ladder_i;
          bi = orphans[orphan_i];
          ci = ladder_i+2;
          mesh_triangles.Add(ai);
          mesh_triangles.Add(bi);
          mesh_triangles.Add(ci);

          ladder_i += 2;
          ladder = mesh_positions[ladder_i];
          if(ladder_i+2 < mesh_positions.Count) rung = mesh_positions[ladder_i+2]; //yes, both this AND previous line need +2 (+2 for advance, +2 for rung)
        }
      }
      left_orphan_i = orphan_i;

      //right
      orphans = right_orphans;
      orphan_i = 0;
      orphan = mesh_positions[orphans[orphan_i]];
      orung = mesh_positions[orphans[orphan_i+1]];
      ladder_i = position_dome_region+1;
      ladder = mesh_positions[ladder_i];
      rung = mesh_positions[ladder_i+2];
      mesh_triangles.Add(orphans[orphan_i]);
      mesh_triangles.Add(ladder_i);
      ladder_i += 2;
      ladder = mesh_positions[ladder_i];
      mesh_triangles.Add(ladder_i);
      while(ladder_i+2 < mesh_positions.Count)
      {
        while((ladder.y > orung.y || rung.z > orung.z) && orphan_i+1 < orphans.Count)
        { //increment orphan
          ai = orphans[orphan_i];
          bi = ladder_i;
          ci = orphans[orphan_i+1];
          mesh_triangles.Add(ai);
          mesh_triangles.Add(bi);
          mesh_triangles.Add(ci);

          orphan_i++;
          orphan = mesh_positions[orphans[orphan_i]];
          if(orphan_i+1 < orphans.Count) orung = mesh_positions[orphans[orphan_i+1]]; //yes, both this AND previous line need +1 (+1 for advance, +1 for orung)
        }
        if(ladder_i+2 < mesh_positions.Count)
        { //increment ladder
          ai = orphans[orphan_i];
          bi = ladder_i;
          ci = ladder_i+2;
          mesh_triangles.Add(ai);
          mesh_triangles.Add(bi);
          mesh_triangles.Add(ci);

          ladder_i += 2;
          ladder = mesh_positions[ladder_i];
          if(ladder_i+2 < mesh_positions.Count) rung = mesh_positions[ladder_i+2]; //yes, both this AND previous line need +2 (+2 for advance, +2 for rung)
        }
      }
      right_orphan_i = orphan_i;
    }

    //fan missing top
    for(int i = left_orphan_i+1; i < left_orphans.Count; i++)
    {
      mesh_triangles.Add(left_orphans[i-1]);
      mesh_triangles.Add(left_orphans[i]);
      mesh_triangles.Add(highest_y_i);
    }
    for(int i = right_orphan_i+1; i < right_orphans.Count; i++)
    {
      mesh_triangles.Add(right_orphans[i]);
      mesh_triangles.Add(right_orphans[i-1]);
      mesh_triangles.Add(highest_y_i);
    }
    mesh_triangles.Add(left_orphans[left_orphans.Count-1]);
    mesh_triangles.Add(right_orphans[right_orphans.Count-1]);
    mesh_triangles.Add(highest_y_i);

    //fill in dome
    int triangle_inner_dome_region = mesh_triangles.Count;
    int position_dome_inner_region = mesh_positions.Count;
    for(int i = position_dome_region; i < position_dome_inner_region; i++) //duplicate inner positions so each can have own normal at seam
    {
      mesh_positions.Add(mesh_positions[i]);
    }
    for(int y = 0; y < concentrated_samples-1; y++)
    {
      mesh_triangles.Add(position_dome_inner_region+y*2+0);
      mesh_triangles.Add(position_dome_inner_region+y*2+2);
      mesh_triangles.Add(position_dome_inner_region+y*2+1);
      mesh_triangles.Add(position_dome_inner_region+y*2+1);
      mesh_triangles.Add(position_dome_inner_region+y*2+2);
      mesh_triangles.Add(position_dome_inner_region+y*2+3);
    }

    //set normals
    mesh_normals = new List<Vector3>(new Vector3[mesh_positions.Count]);
    for(int i = 0; i < triangle_inner_dome_region; i+=3)
    {
      int ai = mesh_triangles[i+0];
      int bi = mesh_triangles[i+1];
      int ci = mesh_triangles[i+2];
      Vector3 a = mesh_positions[ai];
      Vector3 b = mesh_positions[bi];
      Vector3 c = mesh_positions[ci];
      Vector3 n = Vector3.Cross(Vector3.Normalize(b-a),Vector3.Normalize(c-a));
      mesh_normals[ai] = n;
      mesh_normals[bi] = n;
      mesh_normals[ci] = n;
    }

    for(int i = triangle_inner_dome_region; i < mesh_triangles.Count; i+=3)
    {
      int ai = mesh_triangles[i+0];
      int bi = mesh_triangles[i+1];
      int ci = mesh_triangles[i+2];
      Vector3 a = mesh_positions[ai];
      Vector3 b = mesh_positions[bi];
      Vector3 c = mesh_positions[ci];
      Vector3 n = Vector3.Cross(Vector3.Normalize(b-a),Vector3.Normalize(c-a));
      mesh_normals[ai] = n;
      mesh_normals[bi] = n;
      mesh_normals[ci] = n;
    }

    Mesh mesh = new Mesh();
    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
    mesh.vertices = mesh_positions.ToArray();
    mesh.normals = mesh_normals.ToArray();
    mesh.triangles = mesh_triangles.ToArray();

    GameObject gameObject = new GameObject("graph_mesh", typeof(MeshFilter), typeof(MeshRenderer));
    gameObject.transform.parent = graph.transform;
    gameObject.transform.localPosition = new Vector3(0f,0f,0f);
    gameObject.transform.localScale = new Vector3(1f,1f,1f);
    gameObject.transform.localRotation = Quaternion.identity;
    gameObject.GetComponent<MeshFilter>().mesh = mesh;
    gameObject.GetComponent<MeshRenderer>().material = graph_material;
  }

  void reset_state()
  {
    //ensure consistent state
    pressure       = ThermoMath.p_given_percent(0.1); //picked initial pressure somewhat arbitrarily
    temperature    = ThermoMath.t_given_percent(0.9); //picked initial temperature somewhat arbitrarily
    //from this point, the rest should be derived!
    volume         = ThermoMath.v_given_pt(pressure,temperature);
    internalenergy = ThermoMath.u_given_pt(pressure,temperature); //TODO:
    entropy        = ThermoMath.s_given_pt(pressure,temperature); //TODO:
    enthalpy       = ThermoMath.h_given_pt(pressure,temperature); //TODO:
    quality        = ThermoMath.q_given_pt(pressure,temperature); //TODO:

    prev_pressure       = -1;
    prev_temperature    = -1;
    prev_volume         = -1;
    prev_internalenergy = -1;
    prev_entropy        = -1;
    prev_enthalpy       = -1;
    prev_quality        = -1;
  }

  void findObjects()
  {
    vessel    = GameObject.Find("Vessel");
    container = GameObject.Find("Container");
    piston    = GameObject.Find("Piston");
    piston_min_y = piston.transform.localPosition.y;
    piston_max_y = piston_min_y+0.1f; //experimentally derived...
    contents = GameObject.Find("Contents");
    contents_min_h = contents.transform.localScale.y;
    contents_max_h = contents_min_h+0.1f; //experimentally derived...
    water     = GameObject.Find("Water");
    steam     = GameObject.Find("Steam");

    graph     = GameObject.Find("gmodel");
    state_dot = GameObject.Find("gstate");

    text_pressure       = GameObject.Find("text_pressure").GetComponent<TextMeshPro>();
    text_temperature    = GameObject.Find("text_temperature").GetComponent<TextMeshPro>();
    text_volume         = GameObject.Find("text_volume").GetComponent<TextMeshPro>();
    text_internalenergy = GameObject.Find("text_internalenergy").GetComponent<TextMeshPro>();
    text_entropy        = GameObject.Find("text_entropy").GetComponent<TextMeshPro>();
    text_enthalpy       = GameObject.Find("text_enthalpy").GetComponent<TextMeshPro>();
    text_quality        = GameObject.Find("text_quality").GetComponent<TextMeshPro>();
  }

  //assume starting/ending point consistent for whole API!

  //TODO: Remove this whole function ("random_iterate") and all calls to it when math actually completed
  //used for debugging- just "move" in some "random" direction
  //(allows testing connections when underlying math is not yet implemented)
  public void random_iterate()
  {
    int dimension = (int)UnityEngine.Random.Range(0f,3.999f);
    double min_p = 0.0001;
    double max_p = 0.15;
    double delta;
    double deltarange = 0.01;
    switch(dimension)
    {
      case 0: //pressure
        delta = ThermoMath.p_given_percent(deltarange*2)-ThermoMath.p_given_percent(deltarange);
        pressure += (double)UnityEngine.Random.Range(-1f,1f)*delta;
        if(pressure < ThermoMath.p_given_percent(min_p) || pressure > ThermoMath.p_given_percent(max_p)) pressure = ThermoMath.p_given_percent(min_p);
        break;
      case 1: //volume
        delta = ThermoMath.v_given_percent(deltarange*2)-ThermoMath.v_given_percent(deltarange);
        volume += (double)UnityEngine.Random.Range(-1f,1f)*delta;
        if(volume < ThermoMath.v_given_percent(min_p) || volume > ThermoMath.v_given_percent(max_p)) volume = ThermoMath.v_given_percent(min_p);
        break;
      case 2: //temperature
        delta = ThermoMath.t_given_percent(deltarange*2)-ThermoMath.t_given_percent(deltarange);
        temperature += (double)UnityEngine.Random.Range(-1f,1f)*delta;
        if(temperature < ThermoMath.t_given_percent(min_p) || temperature > ThermoMath.t_given_percent(max_p)) temperature = ThermoMath.t_given_percent(min_p);
        break;
      case 3: //quality
        delta = deltarange;
        quality += (double)UnityEngine.Random.Range(-1f,1f)*delta;
        quality = (double)Mathf.Clamp((float)quality,0f,1f);
        break;
    }

    if(System.Double.IsNaN(pressure))    pressure    = ThermoMath.p_given_percent(min_p);
    if(System.Double.IsNaN(volume))      volume      = ThermoMath.v_given_percent(min_p);
    if(System.Double.IsNaN(temperature)) temperature = ThermoMath.t_given_percent(min_p);
  }

  public void add_heat_constant_p(double j) //TODO: implement iteration step
  {
    //no difference between regions

    //default guess
    double new_v = volume;
    double new_u = internalenergy;
    //ITERATE TO SOLVE
    {
      new_v = ThermoMath.v_given_pu(pressure,new_u);
      new_u = internalenergy+(j/mass) - pressure*(new_v-volume);
    }

    //at this point, we have enough internal state to derive the rest
    volume = new_v;
    internalenergy = new_u;
    temperature = ThermoMath.t_given_pv(pressure, volume);
    enthalpy = ThermoMath.h_given_pu(pressure,internalenergy);
    entropy = ThermoMath.s_given_pu(pressure, internalenergy);

    transform_to_state();
  }

  public void add_heat_constant_v(double j)
  {
    //no difference between regions

    double new_u = internalenergy+(j/mass);

    //at this point, we have enough internal state to derive the rest
    internalenergy = new_u;
    pressure = ThermoMath.p_given_vu(volume, internalenergy);
    temperature = ThermoMath.t_given_pv(pressure, volume);
    enthalpy = ThermoMath.h_given_pu(pressure,internalenergy);
    entropy = ThermoMath.s_given_pu(pressure, internalenergy);

    transform_to_state();
  }

  public void add_pressure_insulated(double p, bool insulated) //TODO: get region, implement iteration
  {
    int region = 0; //TODO: get region
    switch(region)
    {
      case 0: //subcooled liquid
      {
        //default guess
        double new_t = temperature;
        double new_u = internalenergy;

        double new_p = pressure+p; //no significant change to other variables!
        //TODO: ITERATE TO SOLVE
        {
        new_t = ThermoMath.t_given_pu(new_p, new_u);
        new_u = ThermoMath.u_given_pt(new_p, new_t);
        }

        //at this point, we have enough internal state to derive the rest
        pressure = new_p;
        temperature = new_t;
        internalenergy = new_u;
        volume = ThermoMath.v_given_pt(pressure, temperature);
        enthalpy = ThermoMath.h_given_pu(pressure,internalenergy);
        entropy = ThermoMath.s_given_pu(pressure, internalenergy);
      }
      break;
      case 1: //two-phase region
      {
        //IGNORE BECAUSE UNSURE HOW TO CALCULATE!
      }
      break;
      case 2: //superheated vapor
      {
        //default guess
        double new_u = internalenergy;
        double new_v = volume;

        double new_p = pressure+p; //no significant change to other variables!
        //TODO: ITERATE TO SOLVE
        {
          //new_u = internalenergy-pressure*volume^k/(k-1)*(volume^(1-k)-new_v^(1-k)); //variable insulation //TODO: if you want to implement variable insulation, change "insulated" from bool to float, then use this eqn (and obv alter the calling functions to pass in variable)
          if(insulated)
            new_u = internalenergy; //unchanged? I got this by subbing k = 1 (sets eqn to u-p*inf*0, which I interpreted as equal to u-p*0, ie, just u)
          else
            new_u = internalenergy-pressure*(volume-new_v); //seems coherent?
          new_v = ThermoMath.v_given_pu(new_p, new_u);
        }

        //at this point, we have enough internal state to derive the rest
        pressure = new_p;
        volume = new_v;
        internalenergy = new_u;
        temperature = ThermoMath.t_given_pu(pressure, internalenergy);
        enthalpy = ThermoMath.h_given_pu(pressure,internalenergy);
        entropy = ThermoMath.s_given_pu(pressure, internalenergy);
      }
      break;
    }

    transform_to_state();
  }

  void transform_to_state()
  {
    state_dot.transform.localPosition = plot(pressure,volume,temperature);

    float size_p = (float)ThermoMath.percent_given_v(volume); //TODO: height shouldn't be based on "percent between min/max volume", but should be geometrically calculated ("what is height of cylinder w/ radius r and volume v?")
    Vector3 piston_lt = piston.transform.localPosition;
    piston_lt.y = piston_min_y+size_p*(piston_max_y-piston_min_y);
    piston.transform.localPosition = piston_lt;

    Vector3 contents_lt = contents.transform.localScale;
    contents_lt.y = contents_min_h+size_p*(contents_max_h-contents_min_h);
    contents.transform.localScale = contents_lt;

    Vector3 water_lt = water.transform.localScale;
    water_lt.y = (float)quality;
    water.transform.localScale = water_lt;
    Vector3 steam_lt = steam.transform.localScale;
    steam_lt.y = 1f-(float)quality;
    steam.transform.localScale = -1f*steam_lt;
  }

  // Update is called once per frame
  void Update()
  {
    //detect editor graphgen modifications
    bool modified = false;
    modified = ((plot_lbase != plot_lbase_prev) || (sample_lbase != sample_lbase_prev));
    sample_lbase_prev = sample_lbase;
    plot_lbase_prev = plot_lbase;
    if(modified) genMesh();

    if(Math.Abs(pressure       - prev_pressure)       > 0.001) text_pressure.SetText(      "P: {0:3}KP",     (float)pressure/1000f);
    if(Math.Abs(temperature    - prev_temperature)    > 0.001) text_temperature.SetText(   "T: {0:3}K",      (float)temperature);
    if(Math.Abs(volume         - prev_volume)         > 0.001) text_volume.SetText(        "v: {0:3}M^3/kg", (float)volume);
    if(Math.Abs(internalenergy - prev_internalenergy) > 0.001) text_internalenergy.SetText("i: {0:3}J/kg",   (float)internalenergy);
    if(Math.Abs(entropy        - prev_entropy)        > 0.001) text_entropy.SetText(       "s: {0:3}J/KgK",  (float)entropy);
    if(Math.Abs(enthalpy       - prev_enthalpy)       > 0.001) text_enthalpy.SetText(      "h: {0:3}J/kg",   (float)enthalpy);
    if(Math.Abs(quality        - prev_quality)        > 0.001) text_quality.SetText(       "q: {0:3}%",      (float)quality);

    prev_pressure       = pressure;
    prev_temperature    = temperature;
    prev_volume         = volume;
    prev_internalenergy = internalenergy;
    prev_entropy        = entropy;
    prev_enthalpy       = enthalpy;
    prev_quality        = quality;
  }

}

