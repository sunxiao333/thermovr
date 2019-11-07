﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

class CMP : IComparer<int>
{
  public List<Vector3> mesh_positions;
  public CMP(List<Vector3> _mesh_positions)
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

public class ThermoMath : MonoBehaviour
{
  //math limits ; xYz = vPt
  //Pa
  public double p_min = IF97.get_Pmin()*1000000.0; // 611.213
  public double p_max = IF97.get_Pmax()*1000000.0; // 100000000
  //Pa
  public double psat_min = IF97.get_ptrip()*1000000.0; // ???
  public double psat_max = IF97.get_pcrit()*1000000.0; // ???
  //M^3/kg
  public double v_min = 1.0/3000;  //0.0003_
  public double v_max = 1.0/0.001; //1000
  //K
  public double t_min = IF97.get_Tmin(); // 273.15
  public double t_max = IF97.get_Tmax(); // 1073.15
  //??
  public double h_min = 0; //0
  public double h_max = 1; //0
  //??
  public double s_min = 0; //0
  public double s_max = 1; //0

  int samples = 350;

  //state
  public double pressure; //pascals
  public double temperature; //kalvin
  public double volume; //M^3/kg
  public double entropy; //?
  public double enthalpy; //?

  //vessel
  GameObject vessel;
  GameObject container;
  GameObject contents;
  GameObject piston;

  //mesh
  GameObject graph;
  GameObject[] graph_bits;
  GameObject state;
  public Material graph_material;
  public GameObject pt_prefab;

  /*
  //IF97 API
  public static double rhomass_Tp(double T, double p)     // Get the mass density [kg/m^3] as a function of T [K] and p [Pa]
  public static double hmass_Tp(double T, double p)       // Get the mass enthalpy [J/kg] as a function of T [K] and p [Pa]
  public static double smass_Tp(double T, double p)       // Get the mass entropy [J/kg/K] as a function of T [K] and p [Pa]
  public static double umass_Tp(double T, double p)       // Get the mass internal energy [J/kg] as a function of T [K] and p [Pa]
  public static double cpmass_Tp(double T, double p)      // Get the mass constant-pressure specific heat [J/kg/K] as a function of T [K] and p [Pa]
  public static double cvmass_Tp(double T, double p)      // Get the mass constant-volume specific heat [J/kg/K] as a function of T [K] and p [Pa]
  public static double speed_sound_Tp(double T, double p) // Get the speed of sound [m/s] as a function of T [K] and p [Pa]
  public static double drhodp_Tp(double T, double p)      // Get the [d(rho)/d(p)]T [kg/mï¿½/Pa] as a function of T [K] and p [Pa]
  //IF97 Paper verified units:
  rhomass_Tp(T,p) | 1.0/rhomass_Tp(300, 3) = 0.00100215 | p:MPa, v:M^3/Kg, T:K | expects:K,MPa returns Kg/M^3
  */

  /*
  //IAPWS95 API
  public static double IAPWS95_pressure(double rho, double T);                                   //Input: rho in kg/m3, T in K, Output: Pa
  public static double IAPWS95_internal_energy(double rho, double T);                            //Input: rho in kg/m3, T in K, Output: Pa
  public static double IAPWS95_entropy(double rho, double T);                                    //Input: rho in kg/m3, T in K, Output: kJ/kg-K
  public static double IAPWS95_enthalpy(double rho, double T);                                   //Input: rho in kg/m3, T in K, Output: kJ/kg
  public static double IAPWS95_isochoric_heat_capacity(double rho, double T);                    //Input: rho in kg/m3, T in K, Output: kJ/kg-K
  public static double IAPWS95_isobaric_heat_capacity(double rho, double T);                     //Input: rho in kg/m3, T in K, Output: kJ/kg-K
  public static double IAPWS95_speed_of_sound(double rho, double T);                             //Input: rho in kg/m3, T in K, Output: m/s
  public static double IAPWS95_joule_thompson_coefficient(double rho, double T);                 //Input: rho in kg/m3, T in K
  public static double IAPWS95_isothermal_throttling_coefficient(double rho, double T);          //Input: rho in kg/m3, T in K
  public static double IAPWS95_isentropic_temperature_pressure_coefficent(double rho, double T); //Input: rho in kg/m3, T in K
  //IAPWS95 Paper verified units:
  IAPWS95_pressure(rho, T) | IAPWS95_pressure(999.887406, 275.0)/1000 = 0.0006982125 | p:MPa, v:Kg/M^3, t:K | expects:Kg/M^3,K returns KPa
  */

  // Start is called before the first frame update
  void Start()
  {
    sample_lbase_prev = sample_lbase;
    plot_lbase_prev = plot_lbase;

    IF97.initRegions();
    findObjects();
    genMesh();
    //genHackMesh();
    //IF97.print_tables();
    //IAPWS95.print_tables();
    //compare_impls();

    reset();
    pressure = Lerpd(p_min,p_max,0.1);
    temperature = Lerpd(t_min,t_max,0.9);
    volume = 1.0/IF97.rhomass_Tp(temperature,pressure/1000000.0);
    /*
    Debug.LogFormat("{0}",pressure);
    Debug.LogFormat("{0}",volume);
    Debug.LogFormat("{0}",temperature);
    */
    dotransform();
  }

  void compare_impls()
  {
    /*
    //passing test
    double t = 373.15; //K
    double v = 17.1969045; //M^3/Kg
    double p = IAPWS95.IAPWS95_pressure(1.0/v,t)*1000; //expects:Kg/M^3,K returns KPa
    Debug.LogFormat("{0,3:E} {1,3:E} {2,3:E}\n", p, v, t);
    v = 1.0/IF97.rhomass_Tp(t,p/1000000); //expects:K,MPa returns Kg/M^3
    Debug.LogFormat("{0,3:E} {1,3:E} {2,3:E}\n", p, v, t);
    */

    //*
    //IF97 primary
    for(int y = 0; y < samples; y++)
    {
      double pt = ((double)y/(samples-1));
      for(int z = 0; z < samples; z++)
      {
        double tt = ((double)z/(samples-1));
        double pst = sample(pt);
        double tst = sample(tt);
        double p = Lerpd(p_min,p_max,pst);
        double t = Lerpd(t_min,t_max,tst);
        double v = 1.0/IF97.rhomass_Tp(t,p/1000000.0); //expects:K,MPa returns Kg/M^3
        //pvt in Pa, M^3/Kg, K
        double _p = IAPWS95.IAPWS95_pressure(1.0/v,t)*1000.0; //expects:Kg/M^3,K returns KPa

        Debug.LogFormat("error:{0} p:{1}Pa ({2}Pa), v:{3}M^3/Kg, t:{4}K",p-_p,p,_p,v,t);
      }
    }
    //*/

    /*
    //IAPWS95 primary
    for(int x = 0; x < samples; x++)
    {
      double vt = ((double)x/(samples-1));
      for(int z = 0; z < samples; z++)
      {
        double tt = ((double)z/(samples-1));
        double vst = sample(vt);
        double tst = sample(tt);
        double v = Lerpd(v_min,v_max,vst);
        double t = Lerpd(t_min,t_max,tst);
        double p = IAPWS95.IAPWS95_pressure(1.0/v,t)*1000.0; //expects:Kg/M^3,K returns KPa
        //pvt in Pa, M^3/Kg, K
        double _v = 1.0/IF97.rhomass_Tp(t,p/1000000.0); //expects:K,MPa returns Kg/M^3

        Debug.LogFormat("error:{0} p:{1}Pa, v:{2}M^3/Kg ({3}M^3/Kg), t:{4}K",v-_v,p,v,_v,t);
      }
    }
    //*/

  }

  double Lerpd(double a, double b, double t) { return (b-a)*t+a; }
  double Clampd(double v, double min, double max) { if(v < min) return min; if(v > max) return max; return v; } //v,min,max ordering mirrors Mathf.Clamp

  //sample bias- "graph density"
  [Range(0.001f,20)]
  public double sample_lbase = 1.6f;
  double sample_lbase_prev = 0.0f;
  double sample(double t) { return Math.Pow(t,sample_lbase); }

  //plot bias- "graph zoom"
  [Range(0.001f,10)]
  public double plot_lbase = 10.0f;
  double plot_lbase_prev = 0.0f;
  float log_plot(double min, double max, double val) { return (float)((Math.Log(val,plot_lbase)-Math.Log(min,plot_lbase))/(Math.Log(max,plot_lbase)-Math.Log(min,plot_lbase))); }

  float plot(double min, double max, double val) { return log_plot(min,max,val); }
  String pv(Vector3 v) { return String.Format("{0}, {1}, {2}",v.x.ToString(".################"),v.y.ToString(".################"),v.z.ToString(".################")); }

  void genMesh()
  {
    int n_pts = samples*samples;
    int n_pts_per_group = 1000;
    int n_groups = (int)Mathf.Ceil(n_pts / n_pts_per_group);
    float pt_size = 0.005f;

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
        double p = Lerpd(p_min,p_max,pst);
        double t = Lerpd(t_min,t_max,tst);
        double v = 1.0/IF97.rhomass_Tp(t,p/1000000.0); //expects:K,MPa returns Kg/M^3
        //pvt in Pa, M^3/Kg, K

        //Debug.LogFormat("p:{0}Pa, v:{1}M^3/Kg, t:{2}K",p,v,t);
        float pplot = plot(p_min,p_max,p);
        float vplot = plot(v_min,v_max,v);
        float tplot = plot(t_min,t_max,t);

        int i = samples*y+z;
        pt_positions[i] = new Vector3(vplot,pplot,tplot);
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
    float highest_y = 0.0f;
    int highest_y_i = 0;
    for(int y = 0; y < concentrated_samples; y++)
    {
      double pt = ((double)y/(concentrated_samples-1));
      double pst = sample(pt);
      double p = Lerpd(psat_min,psat_max,pst);
      double t = IF97.Tsat97(p/1000000.0);
      //pvt in Pa, M^3/Kg, K

      //Debug.LogFormat("p:{0}Pa, v:{1}M^3/Kg, t:{2}K",p,v,t);
      float pplot = plot(p_min,p_max,p);
      if(pplot > highest_y) { highest_y = pplot; highest_y_i = mesh_positions.Count; }
      float tplot = plot(t_min,t_max,t);

      double v;
      float vplot;
      Vector3 point;

      v = 1.0/IF97.rholiq_p(p/1000000.0); //expects:MPa returns Kg/M^3
      vplot = plot(v_min,v_max,v);
      point = new Vector3(vplot,pplot,tplot);
      mesh_positions.Add(point);

      v = 1.0/IF97.rhovap_p(p/1000000.0); //expects:MPa returns Kg/M^3
      vplot = plot(v_min,v_max,v);
      point = new Vector3(vplot,pplot,tplot);
      mesh_positions.Add(point);
    }
    highest_y = Mathf.Lerp(highest_y,1.0f,0.01f); //extra nudge up

    //kill spanning triangles; gather orphans
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

      float x_cmp = (left_ladder.x+right_ladder.x)/2.0f;
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
    CMP cmp = new CMP(mesh_positions);

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
    gameObject.transform.localPosition = new Vector3(0.0f,0.0f,0.0f);
    gameObject.transform.localScale = new Vector3(1.0f,1.0f,1.0f);
    gameObject.transform.localRotation = Quaternion.identity;
    gameObject.GetComponent<MeshFilter>().mesh = mesh;
    gameObject.GetComponent<MeshRenderer>().material = graph_material;
  }

  void genHackMesh()
  {
    int n_pts = samples*samples;
    int n_pts_per_group = 1000;
    int n_groups = (int)Mathf.Ceil(n_pts / n_pts_per_group);
    float pt_size = 0.005f;

    Vector3[] pt_positions;

//*IF97
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
        double p = Lerpd(p_min,p_max,pst);
        double t = Lerpd(t_min,t_max,tst);
        double v = 1.0/IF97.rhomass_Tp(t,p/1000000.0); //expects:K,MPa returns Kg/M^3
        //pvt in Pa, M^3/Kg, K

        //Debug.LogFormat("p:{0}Pa, v:{1}M^3/Kg, t:{2}K",p,v,t);
        float pplot = plot(p_min,p_max,p);
        float vplot = plot(v_min,v_max,v);
        float tplot = plot(t_min,t_max,t);

        int i = samples*y+z;
        pt_positions[i] = new Vector3(vplot,pplot,tplot);
      }
    }
//*/

/*IAPWS95
    //gen positions
    pt_positions = new Vector3[n_pts];
    for(int x = 0; x < samples; x++)
    {
      double vt = ((double)x/(samples-1));
      for(int z = 0; z < samples; z++)
      {
        double tt = ((double)z/(samples-1));
        double vst = sample(vt);
        double tst = sample(tt);
        double v = Lerpd(v_min,v_max,vst);
        double t = Lerpd(t_min,t_max,tst);
        double p = IAPWS95.IAPWS95_pressure(1.0/v,t)*1000.0; //expects:Kg/M^3,K returns KPa
        //pvt in Pa, M^3/Kg, K
        float pplot = plot(p_min,p_max,p);
        float vplot = plot(v_min,v_max,v);
        float tplot = plot(t_min,t_max,t);

        int i = samples*x+z;
        pt_positions[i] = new Vector3(vplot,pplot,tplot);
      }
    }
//*/

/*
    //gen assets
    graph_bits = new GameObject[n_groups];
    GameObject ptfab = (GameObject)Instantiate(pt_prefab);
    int n_pts_remaining = n_pts;
    int n_pts_this_group = n_pts_per_group;
    for(int i = 0; i < n_groups; i++)
    {
      n_pts_this_group = Mathf.Min(n_pts_per_group, n_pts_remaining);
      CombineInstance[] combine = new CombineInstance[n_pts_this_group];

      for(int j = 0; j < n_pts_this_group; j++)
      {
        ptfab.transform.position = pt_positions[i * n_pts_per_group + j];
        ptfab.transform.localScale = new Vector3(pt_size, pt_size, pt_size);

        combine[j].mesh = ptfab.transform.GetComponent<MeshFilter>().mesh;
        combine[j].transform = ptfab.transform.localToWorldMatrix;
      }

      graph_bits[i] = (GameObject)Instantiate(pt_prefab);
      graph_bits[i].transform.parent = graph.transform;
      graph_bits[i].transform.localPosition = new Vector3(0, 0, 0);
      graph_bits[i].transform.localRotation = Quaternion.Euler(0, 0, 0);
      graph_bits[i].transform.localScale = new Vector3(1, 1, 1);
      graph_bits[i].transform.GetComponent<MeshFilter>().mesh = new Mesh();
      graph_bits[i].transform.GetComponent<MeshFilter>().mesh.CombineMeshes(combine);

      n_pts_remaining -= n_pts_this_group;
    }
    Destroy(ptfab, 0f);
//*/

//*
    //HACK
    //gen assets
    graph_bits = new GameObject[n_groups*n_pts_per_group];
    for(int i = 0; i < n_groups*n_pts_per_group; i++)
    {
      graph_bits[i] = (GameObject)Instantiate(pt_prefab);
      graph_bits[i].transform.parent = graph.transform;
      graph_bits[i].transform.localPosition = pt_positions[i];
      graph_bits[i].transform.localScale = new Vector3(pt_size, pt_size, pt_size);
    }
//*/
  }

  void reset()
  {
    //state
    pressure = 0;
    temperature = 0;
    volume = 0;
    entropy = 0;
    enthalpy = 0;
  }

  void findObjects()
  {
    vessel    = GameObject.Find("Vessel");
    container = GameObject.Find("Container");
    contents  = GameObject.Find("Contents");
    piston    = GameObject.Find("Piston");

    graph     = GameObject.Find("Graph");
    state     = GameObject.Find("State");
  }

  //temperature
  public double tp_get_v(double tp)
  {
    return t_get_v(Lerpd(t_min,t_max,tp));
  }
  public double t_get_v(double t)
  {
    temperature = t;
    volume = 1.0/IF97.rhomass_Tp(temperature,pressure/1000000.0); //expects:K,MPa returns Kg/M^3
    dotransform();
    return volume;
  }
  public double tp_get_p(double tp)
  {
    return t_get_p(Lerpd(t_min,t_max,tp));
  }
  public double t_get_p(double t)
  {
    temperature = t;
    //pressure = ???;
    dotransform();
    return pressure;
  }
  //pressure
  public double pp_get_v(double pp)
  {
    return p_get_v(Lerpd(p_min,p_max,pp));
  }
  public double p_get_v(double p)
  {
    pressure = p;
    volume = 1.0/IF97.rhomass_Tp(temperature,pressure/1000000.0); //expects:K,MPa returns Kg/M^3
    dotransform();
    return volume;
  }
  public double pp_get_t(double pp)
  {
    return p_get_t(Lerpd(p_min,p_max,pp));
  }
  public double p_get_t(double p)
  {
    pressure = p;
    //temperature = ???;
    dotransform();
    return temperature;
  }
  //volume
  public double vp_get_t(double vp)
  {
    return v_get_t(Lerpd(v_min,v_max,vp));
  }
  public double v_get_t(double v)
  {
    volume = v;
    //temperature = ???;
    dotransform();
    return temperature;
  }
  public double vp_get_p(double vp)
  {
    return v_get_t(Lerpd(v_min,v_max,vp));
  }
  public double v_get_p(double v)
  {
    volume = v;
    //pressure = ???;
    dotransform();
    return pressure;
  }
  //enthalpy
  public double hp_get_t(double hp)
  {
    return h_get_t(Lerpd(h_min,h_max,hp));
  }
  public double h_get_t(double h)
  {
    enthalpy = h;
    //temperature = ???;
    dotransform();
    return temperature;
  }
  //entropy
  public double sp_get_t(double sp)
  {
    return s_get_t(Lerpd(s_min,s_max,sp));
  }
  public double s_get_t(double s)
  {
    entropy = s;
    //temperature = ???;
    dotransform();
    return temperature;
  }

  void dotransform()
  {
    float pplot = plot(p_min,p_max,pressure);
    float vplot = plot(v_min,v_max,volume);
    float tplot = plot(t_min,t_max,temperature);
    state.transform.localPosition = new Vector3(vplot,pplot,tplot);

    Vector3 piston_lt = piston.transform.localPosition;
    piston_lt.y = (float)((volume-v_min)/(v_max-v_min))/2.0f;
    piston.transform.localPosition = piston_lt;
  }

  // Update is called once per frame
  void Update()
  {
    bool modified = false;
    modified = ((plot_lbase != plot_lbase_prev) || (sample_lbase != sample_lbase_prev));
    sample_lbase_prev = sample_lbase;
    plot_lbase_prev = plot_lbase;
    if(modified)
    {
      /*
      //delete old
      for(int i = 0; i < graph_bits.Length; i++)
        Destroy(graph_bits[i]);
      genHackMesh();
      */
      Destroy(GameObject.Find("graph_mesh"));
      genMesh();
    }
  }

  float cross_fv2z(Vector3 a, Vector3 b) { return (a.x*b.y)-(a.y*b.x); } //z of cross_fv3 with zs set to 0 (good for '2d pt in tri')
  float norm_f(float f) { if(f < 0.0f) return -1.0f; if(f > 0.0f) return 1.0f; return 0.0f; }

  bool pt_in_triangle_inclusive(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
  {
    Vector3 subab = a-b;
    Vector3 subac = a-c;
    Vector3 subba = b-a;
    Vector3 subbc = b-c;
    Vector3 subca = c-a;
    Vector3 subcb = c-b;
    float one   = norm_f(cross_fv2z(subba,p-a));
    float two   = norm_f(cross_fv2z(subba,subca));
    float three = norm_f(cross_fv2z(subcb,p-b));
    float four  = norm_f(cross_fv2z(subcb,subab));
    float five  = norm_f(cross_fv2z(subac,p-c));
    float six   = norm_f(cross_fv2z(subac,subbc));

    return (
      (one == 0.0f || two == 0.0f || one == two)  &&
      (three == 0.0f || four == 0.0f || three == four)  &&
      (five == 0.0f || six == 0.0f || five == six)
    );
  }

  int winding(Vector3 a, Vector3 b, Vector3 c)
  {
      a.x = 0.0f;
      b.x = 0.0f;
      c.x = 0.0f;
      Vector3 cross = Vector3.Cross(a-c,b-c);
      if(cross.x > 0) return 1;
      if(cross.x < 0) return -1;
      return 0;
  }




}

