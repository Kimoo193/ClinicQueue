export type ClinicRole =
  | "Patient"
  | "Receptionist"
  | "Doctor"
  | "ClinicOwner"
  | "SuperAdmin";

export type ClinicMembership = {
  clinicId: string;
  clinicName: string;
  role: string;
};

export type AuthUser = {
  userId: string;
  roles: string[];
};

export type ActiveClinic = {
  id: string;
  name: string;
  roles: string[];
};

export type AuthUserResponse = {
  userId: string;
  roles: string[];
  clinicId?: string | null;
  memberships?: ClinicMembership[] | null;
};

export type PublicClinicProfile = {
  clinicId: string;
  name: string;
  specialty: string;
  subdomainSlug: string;
  logoUrl?: string | null;
  primaryColor?: string | null;
  secondaryColor?: string | null;
};

export type LoginResult =
  | { kind: "ready"; homePath: string }
  | { kind: "choose-clinic" }
  | { kind: "find-clinic" };
